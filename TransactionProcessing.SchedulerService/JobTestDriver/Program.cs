namespace JobTestDriver
{
    using TransactionProcessing.SchedulerService.DataGenerator;
    
    internal class Program{
        static async Task Main(string[] args){

            //List<(String groupName, String streamName, Int64 parkedMessageCount)>? info = await Jobs.GetParkedQueueInformation("esdb://admin:changeit@192.168.0.133:2113?tls=false&tlsVerifyCert=false", CancellationToken.None);
            //foreach ((String groupName, String streamName, Int64 parkedMessageCount) infoItem in info){
            //    Console.WriteLine($"Group: {infoItem.groupName} Stream: {infoItem.streamName} Parked Count: {infoItem.parkedMessageCount}");
            //}

            //List<(Guid, String, DateTime, String)>? incompleteFiles = await Jobs.GetIncompleteFileList("server=192.168.0.133;user id=sa;password=Sc0tland;database=EstateReportingReadModel<estateid>;Encrypt=True;TrustServerCertificate=True", CancellationToken.None);
            //foreach ((Guid, String, DateTime, String) incompleteFile in incompleteFiles){
            //    Console.WriteLine($"FileId: {incompleteFile.Item1} Location: {incompleteFile.Item2} Rcvd: {incompleteFile.Item3} Merchant: {incompleteFile.Item4}");
            //}
            //HttpClient client = new HttpClient();
            //IMessagingServiceClient messagingServiceClient = new MessagingServiceClient(delegate(String s){ return "http://127.0.0.1:5006";}, client);
            //String accessToken = "eyJhbGciOiJSUzI1NiIsImtpZCI6IjQzOUMxRDk5MDUwQTYyMDhEM0U5M0JFMjlBQUJBNzI5IiwidHlwIjoiYXQrand0In0.eyJpc3MiOiJodHRwczovLzEyNy4wLjAuMTo1MDAxIiwibmJmIjoxNjgzMTA2NDE2LCJpYXQiOjE2ODMxMDY0MTYsImV4cCI6MTY4MzExMDAxNiwiYXVkIjpbImVzdGF0ZU1hbmFnZW1lbnQiLCJlc3RhdGVSZXBvcnRpbmciLCJmaWxlUHJvY2Vzc29yIiwibWVzc2FnaW5nU2VydmljZSIsInRyYW5zYWN0aW9uUHJvY2Vzc29yIiwidHJhbnNhY3Rpb25Qcm9jZXNzb3JBQ0wiLCJ2b3VjaGVyTWFuYWdlbWVudCIsImh0dHBzOi8vMTI3LjAuMC4xOjUwMDEvcmVzb3VyY2VzIl0sInNjb3BlIjpbImVzdGF0ZU1hbmFnZW1lbnQiLCJlc3RhdGVSZXBvcnRpbmciLCJmaWxlUHJvY2Vzc29yIiwibWVzc2FnaW5nU2VydmljZSIsInRyYW5zYWN0aW9uUHJvY2Vzc29yIiwidHJhbnNhY3Rpb25Qcm9jZXNzb3JBQ0wiLCJ2b3VjaGVyTWFuYWdlbWVudCJdLCJjbGllbnRfaWQiOiJzZXJ2aWNlQ2xpZW50IiwianRpIjoiMDZBMUI4NzYyRjFGNDJGNkIwMzM5RTYwRTk2MkVDQkUifQ.G5pFWRJF430ZZxnGO_yIxEC6Zj81LRr3HNq6d8V9EV4Pswp5YO7hZ867Ln4mjnrYag4lGI4cpT5S6646r9KNdZMiLOsdQs2LEJPuUjEdVADIwm8rcdqT8OX-sC6uGA6VL0bMmYWQXw1E8d4kax444I6jeNquLjpoWVD1BDp9L1zzC6e_k9W7Fc9MQOogOqO82TXrBl9nkpBbmJQ0HDiub2yVUTUKLwkCeRfBDlyeU8tyNE7kH6IGdHIL_WYUtiiRYjBJ2PNLzTtrXQk4rqw6GB-25K2qcgP5FO0MI675tAkuPKI0DaySXHnAjYssW8wZYy0tkaJL0OIlmOUe-9jM6g";
            //await Jobs.SendSupportEmail(DateTime.Now,
            //                            accessToken,
            //                            "esdb://admin:changeit@192.168.0.133:2113?tls=false&tlsVerifyCert=false",
            //                            "server=192.168.0.133;user id=sa;password=Sc0tland;database=EstateReportingReadModel<estateid>;Encrypt=True;TrustServerCertificate=True",
            //                            new List<String>{
            //                                              "435613ac-a468-47a3-ac4f-649d89764c22"
            //                                          },
            //                            messagingServiceClient,
            //                            CancellationToken.None);

            //HttpClientHandler handler = new HttpClientHandler
            //{
            //    ServerCertificateCustomValidationCallback = (message,
            //                                                 cert,
            //                                                 chain,
            //                                                 errors) =>
            //    {
            //        return true;
            //    }
            //};
            //HttpClient client = new HttpClient(handler);
            //ISecurityServiceClient securityServiceClient = new SecurityServiceClient(delegate (String s) { return "https://192.168.1.167:5001"; }, client);
            //ITransactionProcessorClient transactionProcessorClient = new TransactionProcessorClient(delegate (String s) { return "https://eojrtqfzvyheu0l.m.pipedream.net"; }, client);
            //String transactionProcessorApi = "http://192.168.1.167:5002";
            //String fileProcessorApi = "http://192.168.1.167:5009";
            //String testHostApi = "http://192.168.1.167:9000";
            //String clientId = "serviceClient";
            //String clientSecret = "d192cbc46d834d0da90e8a9d50ded543";

            //ITransactionDataGeneratorService t = new TransactionDataGeneratorService(securityServiceClient,
            //                                                           transactionProcessorClient,
            //                                                           transactionProcessorApi,
            //                                                           fileProcessorApi,
            //                                                           testHostApi,
            //                                                           clientId,
            //                                                           clientSecret,
            //                                                           RunningMode.WhatIf);
            //Guid estateId = Guid.Parse("435613ac-a468-47a3-ac4f-649d89764c22");

            //MakeFloatCreditsJobConfig c = new MakeFloatCreditsJobConfig(clientId,clientSecret, fileProcessorApi,"","", transactionProcessorApi, estateId,
            //    new List<DepositAmount> { new DepositAmount("" ,"", 100) }
            //);

            //await Jobs.GenerateFloatCredits(t, c, CancellationToken.None);
            //Guid merchantId = Guid.Parse("ab1c99fb-1c6c-4694-9a32-b71be5d1da33");
            //await Jobs.GenerateTransactions(t, estateId, merchantId, false, CancellationToken.None);
            ////var d = TransactionDataGenerator.GetTransactionDateTime(new Random(), DateTime.Now);
            ////Console.WriteLine(d);
            //await Jobs.PerformSettlement(t, DateTime.Now,estateId, CancellationToken.None);



        }
    }
}