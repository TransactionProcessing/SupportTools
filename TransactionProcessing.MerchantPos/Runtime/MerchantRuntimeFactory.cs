namespace TransactionProcessing.MerchantPos.Runtime;

public interface IMerchantRuntimeFactory
{
    MerchantRuntime Create(MerchantConfig config);
}

public class MerchantRuntimeFactory : IMerchantRuntimeFactory
{
    private readonly IServiceProvider _serviceProvider;

    public MerchantRuntimeFactory(IServiceProvider serviceProvider)
    {
        this._serviceProvider = serviceProvider;
    }

    public MerchantRuntime Create(MerchantConfig config)
    {
        // Each merchant instance gets a fresh DI scope
        var scope = this._serviceProvider.CreateScope();

        return ActivatorUtilities.CreateInstance<MerchantRuntime>(
            scope.ServiceProvider
        );
    }
}