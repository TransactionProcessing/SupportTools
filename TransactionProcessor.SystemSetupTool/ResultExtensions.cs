using TransactionProcessing.UnionResults;

namespace TransactionProcessor.SystemSetupTool;

internal static class ResultExtensions
{
    internal static Result WithoutPayload<T>(this Result<T> result) =>
        result.Bind(static _ => Results.Ok());
}
