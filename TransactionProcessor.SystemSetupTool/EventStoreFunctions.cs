using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using KurrentDB.Client;
using Shared.Results;
using SimpleResults;

namespace TransactionProcessor.SystemSetupTool;

public class EventStoreFunctions{
    private const string TransactionProcessorGroup = "Transaction Processor";
    private const string TransactionProcessorDomainGroup = "Transaction Processor - Domain";
    private const string TransactionProcessorOrderedGroup = "Transaction Processor - Ordered";
    private const string FileProcessorGroup = "File Processor";
    private const string MessagingServiceGroup = "Messaging Service";

    private readonly KurrentDBProjectionManagementClient ProjectionClient;
    private readonly ICatchupServiceClient CatchupServiceClient;

    public EventStoreFunctions(KurrentDBProjectionManagementClient projectionClient,ICatchupServiceClient catchupServiceClient){
        this.ProjectionClient = projectionClient;
        this.CatchupServiceClient = catchupServiceClient;
    }

    private static PersistentSubscriptionSettings CreatePersistentSettings(Int32 retryCount = 0) => new PersistentSubscriptionSettings(resolveLinkTos: true, maxRetryCount: retryCount, startFrom:new StreamPosition(0));

    public async Task<Result> SetupEventStore(CancellationToken cancellationToken)
    {
        Result projectionResult = await this.DeployProjections(cancellationToken);
        if (projectionResult.IsFailed)
            return ResultHelpers.CreateFailure(projectionResult);
        
        Result indexResult = await this.CreateIndexes(cancellationToken);
        if (indexResult.IsFailed)
            return ResultHelpers.CreateFailure(indexResult);
        
        Result subscriptionsResult = await this.SetupSubscriptions(cancellationToken);
        if (subscriptionsResult.IsFailed)
            return ResultHelpers.CreateFailure(subscriptionsResult);

        return Result.Success();
    }

    private async Task<Result> CreateIndexes(CancellationToken cancellationToken) {

        // Now create any secondary indexes
        String merchantBalanceArchiveIndexName = "merchant-balance-archive-events";
        String merchantBalanceArchiveIndexFilter = "{\r\n  \"filter\": \"rec => rec.value != null && rec.value.merchantId != null && ['MerchantCreatedEvent', 'ManualDepositMadeEvent', 'AutomaticDepositMadeEvent', 'TransactionHasStartedEvent', 'TransactionHasBeenCompletedEvent', 'SettledMerchantFeeAddedToTransactionEvent', 'WithdrawalMadeEvent'].includes(rec.schema.name)\",\r\n  \"fields\": [\r\n    {\r\n      \"name\": \"merchantid\",\r\n      \"selector\": \"rec => rec.value.merchantId\",\r\n      \"type\": \"INDEX_FIELD_TYPE_STRING\"\r\n    }\r\n  ],\r\n  \"start\": true\r\n}";

        CatchupApiResponse<String> indexDetails = await this.CatchupServiceClient.GetIndexAsync(merchantBalanceArchiveIndexName, cancellationToken);

        if (indexDetails.StatusCode == HttpStatusCode.NotFound){
            await this.CatchupServiceClient.CreateIndexAsync(merchantBalanceArchiveIndexName, merchantBalanceArchiveIndexFilter, cancellationToken);
        }

        return Result.Success();
    }

    //private async Task<Result> SetupSubscriptions(CancellationToken cancellationToken){
    //    List<(String streamName, String groupName, Int32 retryCount)> subscriptions = [
    //        ("$ce-TransactionAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-SettlementAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-VoucherAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-FloatAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-MerchantStatementAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-ContractAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-EstateAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-MerchantAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-CallbackMessageAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-ReconciliationAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-FileAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-FileImportLogAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-OperatorAggregate", TransactionProcessorGroup, 0),
    //        ("$ce-MerchantBalanceArchive", TransactionProcessorGroup, 0),

    //        ("$ce-TransactionAggregate", TransactionProcessorDomainGroup, 0),
    //        ("$ce-SettlementAggregate", TransactionProcessorDomainGroup, 0),
    //        ("$ce-FloatAggregate", TransactionProcessorDomainGroup, 0),
    //        ("$ce-MerchantStatementForDateAggregate", TransactionProcessorDomainGroup, 0),

    //        ("$ce-EstateAggregate", TransactionProcessorOrderedGroup, 1),
    //        ("$ce-SettlementAggregate", TransactionProcessorOrderedGroup, 1),
    //        ("$ce-VoucherAggregate", TransactionProcessorOrderedGroup, 1),
    //        ("$ce-TransactionAggregate", TransactionProcessorOrderedGroup, 0),
    //        ("$ce-MerchantStatementAggregate", TransactionProcessorOrderedGroup, 0),
    //        ("$ce-EstateAggregate", TransactionProcessorOrderedGroup, 0),

    //        ("$ce-FileAggregate", FileProcessorGroup, 0),
    //        ("$ce-FileImportLogAggregate", FileProcessorGroup, 0),

    //        ("$ce-EmailAggregate", MessagingServiceGroup, 0),
    //        ("$ce-SMSAggregate", MessagingServiceGroup, 0)
    //    ];

    //    foreach ((String streamName, String groupName, Int32 retryCount) subscription in subscriptions){
    //        Boolean exists = false;
    //        try{
    //            await this.PersistentSubscriptionsClient.GetInfoToStreamAsync(subscription.streamName, subscription.groupName, cancellationToken: cancellationToken, deadline: TimeSpan.FromSeconds(30));
    //            exists = true;
    //        }
    //        catch(PersistentSubscriptionNotFoundException){
    //            exists = false;
    //        }

    //        if (exists == false){
    //            await this.PersistentSubscriptionsClient.CreateToStreamAsync(subscription.streamName, subscription.groupName, CreatePersistentSettings(subscription.retryCount), cancellationToken: cancellationToken, deadline: TimeSpan.FromSeconds(30));
    //        }
    //    }

    //    return Result.Success();
    //}

    private async Task<Result> SetupSubscriptions(CancellationToken cancellationToken)
    {
        // First Step is to setup the endpoints
        EndpointDefinition transactionProcessorEndpoint = new EndpointDefinition( "Transaction Processor", "http://localhost:5002/api/domainevents");
        EndpointDefinition fileProcessorEndpoint = new EndpointDefinition( "File Processor", "http://localhost:5009/api/domainevents");
        EndpointDefinition messagingServiceEndpoint = new EndpointDefinition( "Messaging Service", "http://localhost:5006/api/domainevents");

        List<EndpointDefinition> endpointDefinitions = [transactionProcessorEndpoint, fileProcessorEndpoint, messagingServiceEndpoint];
        
        CatchupApiResponse<IReadOnlyCollection<EndpointDefinition>> existingEndpoints = await this.CatchupServiceClient.GetEndpointsAsync(cancellationToken);
        
        foreach (EndpointDefinition endpointDefinition in endpointDefinitions) {
            if (existingEndpoints.Value.SingleOrDefault(x => x.Name == endpointDefinition.Name) == null)
            {
                var result = await this.CatchupServiceClient.CreateEndpointAsync(endpointDefinition, cancellationToken);
                if (result.IsSuccessStatusCode == false)
                    return Result.Failure($"Error creating endpoint name {endpointDefinition.Name} Status Code = {result.StatusCode}");
            }
        }
        
        // Refresh the list of endpoints now 
        existingEndpoints = await this.CatchupServiceClient.GetEndpointsAsync(cancellationToken);
        
        // Now create any secondary indexes
        String merchantBalanceArchiveIndexName = "merchant-balance-archive-events";
        
        List<(String IndexName, String endpointName, String tag)> subscriptions = [
        ("$idx-ce-EstateAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-TransactionAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-SettlementAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-VoucherAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-FloatAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-MerchantStatementAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-SettlementAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-ContractAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-MerchantAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-CallbackMessageAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-ReconciliationAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-FileAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-FileImportLogAggregate", "Transaction Processor", "Main"),
        ("$idx-ce-OperatorAggregate", "Transaction Processor", "Main"),
        
        ("$idx-ce-TransactionAggregate", "Transaction Processor", "Domain"),
        ("$idx-ce-SettlementAggregate", "Transaction Processor", "Domain"),
        ("$idx-ce-FloatAggregate", "Transaction Processor", "Domain"),
        ("$idx-ce-MerchantStatementForDateAggregate", "Transaction Processor", "Domain"),

        ("$idx-ce-EstateAggregate", "Transaction Processor", "Ordered"),
        ("$idx-ce-SettlementAggregate", "Transaction Processor", "Ordered"),
        ("$idx-ce-VoucherAggregate", "Transaction Processor", "Ordered"),
        ("$idx-ce-TransactionAggregate", "Transaction Processor", "Ordered"),
        ("$idx-ce-MerchantStatementAggregate", "Transaction Processor", "Ordered"),
        ($"$idx-user-{merchantBalanceArchiveIndexName}", "Transaction Processor", "Ordered"),
        
        ("$idx-ce-FileAggregate", "File Processor", "Main"),
        ("$idx-ce-FileImportLogAggregate", "File Processor", "Main"),
        
        ("$idx-ce-EmailAggregate", "Messaging Service", "Main"),
        ("$idx-ce-SMSAggregate", "Messaging Service", "Main"),
        ];

        foreach ((String IndexName, String endpointName, String tag) subscription in subscriptions)
        {
            var endpoint = existingEndpoints.Value.SingleOrDefault(x => x.Name == subscription.endpointName);
            if (endpoint == null)
                return Result.Failure($"Endpoint name {subscription.endpointName} not found");
            // build the subscription id
            String subscriptionId = $"{subscription.IndexName}_{subscription.endpointName}_{subscription.tag}";

            CatchupApiResponse<IReadOnlyCollection<SubscriptionDefinition>> subscriptionConfigList = await this.CatchupServiceClient.GetSubscriptionConfigurationsAsync(cancellationToken);

            if (subscriptionConfigList.Value.SingleOrDefault(x => x.SubscriptionId == subscriptionId) == null) {
                SubscriptionConfigurationRequest subscriptionConfigurationRequest = new();
                subscriptionConfigurationRequest = subscriptionConfigurationRequest with {
                    ContinueOnParked = true,
                    CheckpointBatchSize = 100,
                    EndpointUrl = endpoint.Url,
                    RetryDelaySeconds = 5,
                    RetryMaxAttempts = 0,
                    SecondaryIndexName = subscription.IndexName,
                    SoftDeleteParked = true,
                    SubscriptionId = subscriptionId,
                    Tag = subscription.tag,
                    TimeoutSeconds = 30,
                };

                var result = await this.CatchupServiceClient.CreateSubscriptionConfigurationAsync(subscriptionConfigurationRequest, cancellationToken);
                if (result.IsSuccessStatusCode == false)
                    return Result.Failure($"Error subscription configuration id {subscriptionConfigurationRequest.SubscriptionId} Status Code = {result.StatusCode}");
            }
        }

        return Result.Success();
    }

    private async Task<Result> DeployProjections(CancellationToken cancellationToken) {
        IAsyncEnumerable<ProjectionDetails> currentProjectionsList = this.ProjectionClient.ListAllAsync(cancellationToken: cancellationToken);
        var currentProjections = new List<ProjectionDetails>();

        await foreach (var item in currentProjectionsList.WithCancellation(cancellationToken))
        {
            currentProjections.Add(item);
        }

        var projectionsToDeploy = Directory.GetFiles("projections/continuous");

        foreach (var projection in projectionsToDeploy)
        {
            if (projection.Contains("EstateManagementSubscriptionStreamBuilder", StringComparison.Ordinal) ||
                projection.Contains("FileProcessorSubscriptionStreamBuilder", StringComparison.Ordinal) ||
                projection.Contains("TransactionProcessorSubscriptionStreamBuilder", StringComparison.Ordinal) ||
                projection.Contains("EstateAggregator", StringComparison.Ordinal) ||
                projection.Contains("MerchantAggregator", StringComparison.Ordinal) ||
                projection.Contains("MerchantBalanceAggregator", StringComparison.Ordinal) ||
                projection.Contains("CallbackHandlerEnricher", StringComparison.Ordinal))
            {
                continue;
            }

            FileInfo f = new FileInfo(projection);
            String name = Path.GetFileNameWithoutExtension(f.Name);
            var body = File.ReadAllText(f.FullName);

            var x = body.IndexOf("//endtestsetup", StringComparison.Ordinal);
            x = x + "//endtestsetup".Length;

            body = body.Substring(x);

                if (currentProjections.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal)) == false)
                {
                    await this.ProjectionClient.CreateContinuousAsync(name, body, true, cancellationToken: cancellationToken);
                }
                else
                {
                    await this.ProjectionClient.DisableAsync(name, cancellationToken: cancellationToken);
                    await this.ProjectionClient.UpdateAsync(name, body, true, cancellationToken: cancellationToken);
                    await this.ProjectionClient.EnableAsync(name, cancellationToken: cancellationToken);
            }
        }

        return Result.Success();
    }
}
