using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using SimpleResults;

namespace TransactionProcessor.SystemSetupTool;

public class EventStoreFunctions{
    private readonly EventStoreProjectionManagementClient ProjectionClient;

    private readonly EventStorePersistentSubscriptionsClient PersistentSubscriptionsClient;

    public EventStoreFunctions(EventStoreProjectionManagementClient projectionClient,EventStorePersistentSubscriptionsClient persistentSubscriptionsClient){
        this.ProjectionClient = projectionClient;
        this.PersistentSubscriptionsClient = persistentSubscriptionsClient;
    }

    private static PersistentSubscriptionSettings CreatePersistentSettings(Int32 retryCount = 0) => new PersistentSubscriptionSettings(resolveLinkTos: true, maxRetryCount: retryCount, startFrom:new StreamPosition(0));

    public async Task<Result> SetupEventStore(CancellationToken cancellationToken)
    {
        await this.DeployProjections(cancellationToken);
        await this.SetupSubscriptions(cancellationToken);
        
        return Result.Success();
    }

    private async Task<Result> SetupSubscriptions(CancellationToken cancellationToken){
        List<(String streamName, String groupName, Int32 retryCount)> subscriptions = [
            ("$ce-TransactionAggregate", "Transaction Processor", 0),
            ("$ce-SettlementAggregate", "Transaction Processor", 0),
            ("$ce-VoucherAggregate", "Transaction Processor", 0),
            ("$ce-FloatAggregate", "Transaction Processor", 0),
            ("$ce-MerchantStatementAggregate", "Transaction Processor", 0),
            ("$ce-ContractAggregate", "Transaction Processor", 0),
            ("$ce-EstateAggregate", "Transaction Processor", 0),
            ("$ce-MerchantAggregate", "Transaction Processor", 0),
            ("$ce-CallbackMessageAggregate", "Transaction Processor", 0),
            ("$ce-ReconciliationAggregate", "Transaction Processor", 0),
            ("$ce-FileAggregate", "Transaction Processor", 0),
            ("$ce-FileImportLogAggregate", "Transaction Processor", 0),
            ("$ce-OperatorAggregate", "Transaction Processor", 0),

            ("$ce-TransactionAggregate", "Transaction Processor - Domain", 0),
            ("$ce-SettlementAggregate", "Transaction Processor - Domain", 0),
            ("$ce-FloatAggregate", "Transaction Processor - Domain", 0),
            ("$ce-MerchantStatementForDateAggregate", "Transaction Processor - Domain", 0),

            ("$ce-EstateAggregate", "Transaction Processor - Ordered", 1),
            ("$ce-SettlementAggregate", "Transaction Processor - Ordered", 1),
            ("$ce-VoucherAggregate", "Transaction Processor - Ordered", 1),
            ("$ce-TransactionAggregate", "Transaction Processor - Ordered", 0),
            ("$ce-MerchantStatementAggregate", "Transaction Processor - Ordered", 0),
            ("$ce-EstateAggregate", "Transaction Processor - Ordered", 0),

            ("$ce-FileAggregate", "File Processor", 0),
            ("$ce-FileImportLogAggregate", "File Processor", 0),

            ("$ce-EmailAggregate", "Messaging Service", 0),
            ("$ce-SMSAggregate", "Messaging Service", 0)
        ];

        foreach ((String streamName, String groupName, Int32 retryCount) subscription in subscriptions){
            Boolean exists = false;
            try{
                PersistentSubscriptionInfo subscriptionInfo = await this.PersistentSubscriptionsClient.GetInfoToStreamAsync(subscription.streamName, subscription.groupName, cancellationToken: cancellationToken, deadline:TimeSpan.FromSeconds(30));
                exists = true;
            }
            catch(PersistentSubscriptionNotFoundException pex){
                exists = false;
            }

            if (exists == false){
                await this.PersistentSubscriptionsClient.CreateToStreamAsync(subscription.streamName, subscription.groupName, CreatePersistentSettings(subscription.retryCount), cancellationToken: cancellationToken, deadline: TimeSpan.FromSeconds(30));
            }
        }

        return Result.Success();
    }
    private async Task<Result> DeployProjections(CancellationToken cancellationToken)
    {
        var currentProjections = await this.ProjectionClient.ListAllAsync(cancellationToken: cancellationToken).ToListAsync(cancellationToken);

        var projectionsToDeploy = Directory.GetFiles("projections/continuous");

        foreach (var projection in projectionsToDeploy)
        {
            if (projection.Contains("EstateManagementSubscriptionStreamBuilder") ||
                projection.Contains("FileProcessorSubscriptionStreamBuilder") ||
                projection.Contains("TransactionProcessorSubscriptionStreamBuilder") ||
                projection.Contains("EstateAggregator") ||
                projection.Contains("MerchantAggregator") ||
                projection.Contains("MerchantBalanceAggregator"))
            {
                continue;
            }

            FileInfo f = new FileInfo(projection);
            String name = f.Name.Substring(0, f.Name.Length - (f.Name.Length - f.Name.LastIndexOf(".")));
            var body = File.ReadAllText(f.FullName);

            var x = body.IndexOf("//endtestsetup");
            x = x + "//endtestsetup".Length;

            body = body.Substring(x);

            // Is this already deployed (in the master list)
            if (currentProjections.Any(p => p.Name == name) == false)
            {
                // Projection does not exist so create
                await this.ProjectionClient.CreateContinuousAsync(name, body, true, cancellationToken: cancellationToken);
            }
            else
            {
                // Already exists so we need to update but do not reset
                await this.ProjectionClient.DisableAsync(name, cancellationToken: cancellationToken);
                await this.ProjectionClient.UpdateAsync(name, body, true, cancellationToken: cancellationToken);
                await this.ProjectionClient.EnableAsync(name, cancellationToken: cancellationToken);
            }
        }

        return Result.Success();
    }
}