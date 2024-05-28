namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataGeneration;
using EstateManagement.DataTransferObjects.Responses.Contract;
using EstateManagement.DataTransferObjects.Responses.Merchant;
using EventStore.Client;
using MessagingService.Client;
using MessagingService.DataTransferObjects;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Quartz;
using Shared.Logger;

public static class Jobs{
    public static async Task GenerateMerchantStatements(ITransactionDataGenerator t, Guid estateId, CancellationToken cancellationToken){
        List<MerchantResponse> merchants = await t.GetMerchants(estateId, cancellationToken);

        if (merchants.Any() == false){
            throw new JobExecutionException($"No merchants returned for Estate [{estateId}]");
        }

        List<String> results = new List<String>();
        foreach (MerchantResponse merchantResponse in merchants)
        {
            Boolean success = await t.GenerateMerchantStatement(merchantResponse.EstateId, merchantResponse.MerchantId, DateTime.Now, cancellationToken);
            if (success == false){
                results.Add(merchantResponse.MerchantName);
            }
        }

        if (results.Any()){
            throw new JobExecutionException($"Error generating statements for merchants [{String.Join(",", results)}]");
        }
    }

    public static async Task GenerateFileUploads(ITransactionDataGenerator t, Guid estateId, Guid merchantId, Guid userId, CancellationToken cancellationToken)
    {
        MerchantResponse merchant = await t.GetMerchant(estateId, merchantId, cancellationToken);

        if (merchant == default)
        {
            throw new JobExecutionException($"No merchant returned for Estate Id [{estateId}] Merchant Id [{merchantId}]");
        }

        List<ContractResponse> contracts = await t.GetMerchantContracts(merchant, cancellationToken);
        DateTime fileDate = DateTime.Now.Date;
        List<String> results = new List<String>();
        foreach (ContractResponse contract in contracts)
        {
            // Generate a file and upload
            Boolean success = await t.SendUploadFile(fileDate, contract, merchant,userId, cancellationToken);

            if (success == false)
            {
                results.Add(contract.OperatorName);
            }
        }

        if (results.Any())
        {
            throw new JobExecutionException($"Error uploading files for merchant [{merchant.MerchantName}] [{String.Join(",", results)}]");
        }
    }

    public static async Task GenerateTransactions(ITransactionDataGenerator t, Guid estateId, Guid merchantId, Boolean requireLogon, CancellationToken cancellationToken){
        // get the merchant
        MerchantResponse merchant = await t.GetMerchant(estateId, merchantId, cancellationToken);

        if (merchant == default){
            throw new JobExecutionException($"Error getting Merchant Id [{merchantId}] for Estate Id [{estateId}]");
        }

        DateTime transactionDate = DateTime.Now;

        // Get the merchants contracts
        List<ContractResponse> contracts = await t.GetMerchantContracts(merchant, cancellationToken);

        if (contracts.Any() == false)
        {
            throw new JobExecutionException($"No contracts returned for Merchant [{merchant.MerchantName}]");
        }

        if (requireLogon)
        {
            // Do a logon transaction for the merchant
            Boolean logonSuccess = await t.PerformMerchantLogon(transactionDate, merchant, cancellationToken);

            if (logonSuccess == false)
            {
                throw new JobExecutionException($"Error performing logon for Merchant [{merchant.MerchantName}]");
            }
        }
        Random r = new Random();
        List<String> results = new List<String>();
        foreach (ContractResponse contract in contracts)
        {

            Int32 numberOfSales = r.Next(2, 4);
            // Generate and send some sales
            Boolean success = await t.SendSales(transactionDate, merchant, contract, numberOfSales, cancellationToken);

            if (success == false)
            {
                results.Add(contract.OperatorName);
            }
        }

        if (results.Any())
        {
            throw new JobExecutionException($"Error sending sales files for merchant [{merchant.MerchantName}] [{String.Join(",", results)}]");
        }
    }

    public static async Task PerformSettlement(ITransactionDataGenerator t, DateTime dateTime, Guid estateId, CancellationToken cancellationToken)
    {
        Boolean success = await t.PerformSettlement(dateTime.Date, estateId, cancellationToken);

        if (success == false){
            throw new JobExecutionException($"Error performing settlement for Estate Id [{estateId}] and date [{dateTime:dd-MM-yyyy}]");
        }
    }

    public static async Task<List<(String groupName, String streamName, Int64 parkedMessageCount)>> GetParkedQueueInformation(String eventStoreConnectionString, CancellationToken cancellationToken){
        EventStoreClientSettings clientSettings = EventStoreClientSettings.Create(eventStoreConnectionString);
        EventStore.Client.EventStorePersistentSubscriptionsClient client = new EventStorePersistentSubscriptionsClient(clientSettings);
        List<(String groupName, String streamName, Int64 parkedMessageCount)> result = new List<(String groupName, String streamName, Int64 parkedMessageCount)>();
        IEnumerable<PersistentSubscriptionInfo> x = await client.ListAllAsync(cancellationToken:cancellationToken);
        foreach (PersistentSubscriptionInfo persistentSubscriptionInfo in x){
            if (persistentSubscriptionInfo.Stats.ParkedMessageCount > 0){
                // Add to replay list
                result.Add((persistentSubscriptionInfo.GroupName, persistentSubscriptionInfo.EventSource, persistentSubscriptionInfo.Stats.ParkedMessageCount));
            }
        }
        
        return result;
    }

    public static async Task<List<(Guid,String,DateTime,String)>> GetIncompleteFileList(String databaseConnectionString,CancellationToken cancellationToken){
        List<(Guid, String, DateTime, String)> result = new List<(Guid, String, DateTime, String)>();
        
        using (SqlConnection connection = new SqlConnection(databaseConnectionString)){
            await connection.OpenAsync(cancellationToken);

            SqlCommand command = connection.CreateCommand();
            command.CommandText = "select FileId, FileLocation, FileReceivedDateTime, merchant.Name from [file] f inner join merchant on merchant.MerchantId = f.MerchantId where IsCompleted= 0";
            command.CommandType = CommandType.Text;

            var reader = await command.ExecuteReaderAsync(cancellationToken);
            
            while (await reader.ReadAsync(cancellationToken)){

                var fileId = reader.GetGuid(0);
                var fileLocation = reader.GetString(1);
                var fileReceivedDateTime = reader.GetDateTime(2);
                var merchantName = reader.GetString(3);

                result.Add((fileId, fileLocation, fileReceivedDateTime, merchantName));
            }
        }

        return result;
    }

    public static SendEmailRequest BuildSupportEmail(DateTime dateTime,
                                                                 List<(Guid fileId, String fileLocation, DateTime fileReceivedDateTime, String merchantName)> incompleteFiles,
                                                                 List<(String groupName, String streamName, Int64 parkedMessageCount)> parkedQueueInformation)
    {
        // Build uo the HTML String 
        StringBuilder htmlBuilder = new StringBuilder();

        htmlBuilder.AppendLine("<html>");
        htmlBuilder.AppendLine("<head>");
        htmlBuilder.AppendLine("<style>");
        htmlBuilder.AppendLine("table, th, td { border: 1px solid black; }");
        htmlBuilder.AppendLine("</style>");
        htmlBuilder.AppendLine("</style>");
        htmlBuilder.AppendLine("</head>");

        htmlBuilder.AppendLine("<body>");

        htmlBuilder.AppendLine($"<h1>Daily Support Report for {dateTime.ToString("dd-MM-yyyy")}</h1>");

        htmlBuilder.AppendLine("<h2>Parked Messages Stats</h2>");

        if (parkedQueueInformation.Any() == false){
            htmlBuilder.AppendLine("<p>No Data</p>");
        }
        else{
            htmlBuilder.AppendLine("<table>");
            htmlBuilder.Append("<tr><th>Group</th><th>Stream</th><th>Parked Messages</th></tr>");

            foreach ((String groupName, String streamName, Int64 parkedMessageCount) info in parkedQueueInformation){
                htmlBuilder.Append($"<tr><td>{info.groupName}</td><td>{info.streamName}</td><td>{info.parkedMessageCount}</td></tr>");
            }

            htmlBuilder.AppendLine("</table>");
        }

        htmlBuilder.AppendLine("<h2>Incomplete Bulk Files</h2>");

        if (incompleteFiles.Any() == false)
        {
            htmlBuilder.AppendLine("<p>No Data</p>");
        }
        else
        {
            htmlBuilder.AppendLine("<table>");
            htmlBuilder.Append("<tr><th>Id</th><th>Location</th><th>File Received</th><th>Merchant Name</th></tr>");

            foreach ((Guid fileId, String fileLocation, DateTime fileReceivedDateTime, String merchantName) incompleteFile in incompleteFiles){
             
                htmlBuilder.Append($"<tr><td>{incompleteFile.fileId}</td><td>{incompleteFile.fileLocation}</td><td>{incompleteFile.fileReceivedDateTime}</td><td>{incompleteFile.merchantName}</td></tr>");
            }

            htmlBuilder.AppendLine("</table>");
        }

        htmlBuilder.AppendLine("</body>");
        htmlBuilder.AppendLine("</html>");

        SendEmailRequest request = new SendEmailRequest{
                                                           Body = htmlBuilder.ToString(),
                                                           ConnectionIdentifier = Guid.NewGuid(),
                                                           FromAddress = "support@transactionprocessing.com",
                                                           IsHtml = true,
                                                           MessageId = Guid.NewGuid(),
                                                           Subject = $"Daily Support Report for {dateTime:dd-MM-yyyy}",
                                                           ToAddresses = new List<String>{
                                                                                             "stuart_ferguson1development@outlook.com"
                                                                                         }
                                                       };
        return request;
    }

    public static async Task SendSupportEmail(DateTime dateTime,
                                              String accessToken,
                                              String eventStoreConnectionString, String databaseConnectionString, List<String> estateIds, 
                                              IMessagingServiceClient messagingServiceClient,
                                              CancellationToken cancellationToken)
    {
        List<(String groupName, String streamName, Int64 parkedMessageCount)> parkedQueueInfo = await Jobs.GetParkedQueueInformation(eventStoreConnectionString, cancellationToken);
        List<(Guid, String, DateTime, String)> incompleteFiles = new List<(Guid, String, DateTime, String)>();
        foreach (String estateId in estateIds){
            String connectionString = databaseConnectionString.Replace("<estateid>", estateId.ToString());
            incompleteFiles.AddRange(await GetIncompleteFileList(connectionString, cancellationToken));
        }

        SendEmailRequest emailRequest = BuildSupportEmail(dateTime, incompleteFiles, parkedQueueInfo);
        emailRequest.ConnectionIdentifier = Guid.Parse(estateIds.First());
        await messagingServiceClient.SendEmail(accessToken, emailRequest, CancellationToken.None);
    }
}