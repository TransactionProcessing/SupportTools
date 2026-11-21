using TransactionProcessing.UnionResults;
using Results = TransactionProcessing.UnionResults.Results;

namespace TransactionProcessing.MerchantPos.Runtime;

public static class ResultExtensions
{
    public static Result FailureExtended(string message, Exception exception)
    {
        return new ErrorList([
            new Error(ErrorType.Failure, message),
            new Error(ErrorType.Failure, exception.Message)
        ]);
    }

    internal static Result<T> AsFailure<T>(object? outcome) => outcome switch
    {
        Error error => error,
        ErrorList errors => errors,
        _ => new Error(ErrorType.Critical, "An uninitialized result has no outcome.")
    };

    internal static Result WithoutPayload<T>(this Result<T> result) =>
        result.Bind(static _ => Results.Ok());
}
