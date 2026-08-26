using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using KurrentDB.Client;
using SimpleResults;

namespace TransactionProcessor.SystemSetupTool;

public class EventStoreFunctions{
    private const string TransactionProcessorGroup = "Transaction Processor";
    private const string TransactionProcessorDomainGroup = "Transaction Processor - Domain";
    private const string TransactionProcessorOrderedGroup = "Transaction Processor - Ordered";
    private const string FileProcessorGroup = "File Processor";
    private const string MessagingServiceGroup = "Messaging Service";

    private readonly KurrentDBProjectionManagementClient ProjectionClient;

    private readonly KurrentDBPersistentSubscriptionsClient PersistentSubscriptionsClient;

    public EventStoreFunctions(KurrentDBProjectionManagementClient projectionClient, KurrentDBPersistentSubscriptionsClient persistentSubscriptionsClient){
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
            ("$ce-TransactionAggregate", TransactionProcessorGroup, 0),
            ("$ce-SettlementAggregate", TransactionProcessorGroup, 0),
            ("$ce-VoucherAggregate", TransactionProcessorGroup, 0),
            ("$ce-FloatAggregate", TransactionProcessorGroup, 0),
            ("$ce-MerchantStatementAggregate", TransactionProcessorGroup, 0),
            ("$ce-ContractAggregate", TransactionProcessorGroup, 0),
            ("$ce-EstateAggregate", TransactionProcessorGroup, 0),
            ("$ce-MerchantAggregate", TransactionProcessorGroup, 0),
            ("$ce-CallbackMessageAggregate", TransactionProcessorGroup, 0),
            ("$ce-ReconciliationAggregate", TransactionProcessorGroup, 0),
            ("$ce-FileAggregate", TransactionProcessorGroup, 0),
            ("$ce-FileImportLogAggregate", TransactionProcessorGroup, 0),
            ("$ce-OperatorAggregate", TransactionProcessorGroup, 0),
            ("$ce-MerchantBalanceArchive", TransactionProcessorGroup, 0),

            ("$ce-TransactionAggregate", TransactionProcessorDomainGroup, 0),
            ("$ce-SettlementAggregate", TransactionProcessorDomainGroup, 0),
            ("$ce-FloatAggregate", TransactionProcessorDomainGroup, 0),
            ("$ce-MerchantStatementForDateAggregate", TransactionProcessorDomainGroup, 0),

            ("$ce-EstateAggregate", TransactionProcessorOrderedGroup, 1),
            ("$ce-SettlementAggregate", TransactionProcessorOrderedGroup, 1),
            ("$ce-VoucherAggregate", TransactionProcessorOrderedGroup, 1),
            ("$ce-TransactionAggregate", TransactionProcessorOrderedGroup, 0),
            ("$ce-MerchantStatementAggregate", TransactionProcessorOrderedGroup, 0),
            ("$ce-EstateAggregate", TransactionProcessorOrderedGroup, 0),

            ("$ce-FileAggregate", FileProcessorGroup, 0),
            ("$ce-FileImportLogAggregate", FileProcessorGroup, 0),

            ("$ce-EmailAggregate", MessagingServiceGroup, 0),
            ("$ce-SMSAggregate", MessagingServiceGroup, 0)
        ];

        foreach ((String streamName, String groupName, Int32 retryCount) subscription in subscriptions){
            Boolean exists = false;
            try{
                await this.PersistentSubscriptionsClient.GetInfoToStreamAsync(subscription.streamName, subscription.groupName, cancellationToken: cancellationToken, deadline: TimeSpan.FromSeconds(30));
                exists = true;
            }
            catch(PersistentSubscriptionNotFoundException){
                exists = false;
            }

            if (exists == false){
                await this.PersistentSubscriptionsClient.CreateToStreamAsync(subscription.streamName, subscription.groupName, CreatePersistentSettings(subscription.retryCount), cancellationToken: cancellationToken, deadline: TimeSpan.FromSeconds(30));
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
