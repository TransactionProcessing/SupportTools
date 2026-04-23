using Shared.Logger;

namespace TransactionProcessing.MerchantFileProcessor.Services;

public static class LocalLogger
{
    public static Context For(string? merchantId = null, Guid? runId = null) => new(merchantId, runId);

    public static void LogInformation(string? merchantId, Guid? runId, string message) =>
        Logger.LogInformation(FormatMessage(merchantId, runId, message));

    public static void LogWarning(string? merchantId, Guid? runId, string message) =>
        Logger.LogWarning(FormatMessage(merchantId, runId, message));

    public static void LogError(string? merchantId, Guid? runId, string message, Exception exception) =>
        Logger.LogError(FormatMessage(merchantId, runId, message), exception);

    private static string FormatMessage(string? merchantId, Guid? runId, string message)
    {
        var merchantColumn = merchantId ?? string.Empty;
        var runColumn = runId?.ToString() ?? string.Empty;

        return $"|{merchantColumn}|{runColumn}|{message}";
    }

    public sealed class Context(string? merchantId, Guid? runId)
    {
        public Context WithRun(Guid? value) => new(merchantId, value);

        public void LogInformation(string message) => LocalLogger.LogInformation(merchantId, runId, message);

        public void LogWarning(string message) => LocalLogger.LogWarning(merchantId, runId, message);

        public void LogError(string message, Exception exception) => LocalLogger.LogError(merchantId, runId, message, exception);
    }
}
