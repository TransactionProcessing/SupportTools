using System.Runtime.CompilerServices;

namespace TransactionProcessing.MerchantPos.Tests;

internal static class TestLogging
{
    [ModuleInitializer]
    internal static void Initialise()
    {
        var logFactory = new NLog.LogFactory();
        TransactionProcessing.Shared.Logger.Logger.Initialise(logFactory.GetLogger("MerchantPos.Tests"), "MerchantPos.Tests");
    }
}
