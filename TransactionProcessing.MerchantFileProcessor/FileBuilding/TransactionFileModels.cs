using System.Globalization;
using TransactionProcessing.MerchantFileProcessor.Configuration;

namespace TransactionProcessing.MerchantFileProcessor.FileBuilding;

public sealed record GeneratedFile(
    string FileName,
    byte[] Content,
    string ContentType,
    int RecordCount,
    decimal TotalAmount,
    string FileProfileId,
    string Format,
    IReadOnlyList<GeneratedTransaction> Transactions);

public sealed record GeneratedTransaction(
    string MerchantId,
    string ContractId,
    string ProductCode,
    string Description,
    string RecipientMobileNumber,
    int Quantity,
    decimal UnitAmount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset TransactionDateUtc);

public sealed record TransactionFileContext(
    MerchantOptions Merchant,
    ContractOptions Contract,
    GeneratedTransaction? Transaction,
    DateTimeOffset ProcessingTimestampUtc,
    int RecordCount,
    decimal FileTotalAmount);

public interface ITransactionGenerator
{
    IReadOnlyList<GeneratedTransaction> GenerateTransactions(
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset processingTimestampUtc);
}

public sealed class RandomTransactionGenerator(
    MerchantProcessingOptions options) : ITransactionGenerator
{
    public IReadOnlyList<GeneratedTransaction> GenerateTransactions(
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset processingTimestampUtc)
    {
        var fixedValueProducts = contract.Products
            .Where(product => product.IsFixedValue)
            .ToArray();

        if (fixedValueProducts.Length == 0)
        {
            throw new InvalidOperationException(
                $"Contract '{contract.ContractId}' for merchant '{merchant.MerchantId}' does not contain any fixed-value products.");
        }

        var transactionCount = Random.Shared.Next(
            options.TransactionGeneration.MinimumTransactionsPerContract,
            options.TransactionGeneration.MaximumTransactionsPerContract + 1);

        return Enumerable.Range(0, transactionCount)
            .Select(_ =>
            {
                var product = fixedValueProducts[Random.Shared.Next(fixedValueProducts.Length)];
                var totalAmount = product.Quantity * product.UnitAmount;

                return new GeneratedTransaction(
                    merchant.MerchantId,
                    contract.ContractId,
                    product.ProductCode,
                    product.Description,
                    BuildMobileNumber(),
                    product.Quantity,
                    product.UnitAmount,
                    totalAmount,
                    product.Currency,
                    processingTimestampUtc);
            })
            .ToArray();
    }

    private static string BuildMobileNumber() =>
        $"07{Random.Shared.NextInt64(0, 1_000_000_000):D9}";
}

public static class TransactionFileFieldResolver
{
    public static string GetTextValue(FileFieldOptions field, TransactionFileContext context)
    {
        return GetValue(field, context)?.ToString() ?? string.Empty;
    }

    public static object GetValue(FileFieldOptions field, TransactionFileContext context)
    {
        if (!string.IsNullOrWhiteSpace(field.Value))
        {
            return field.Value;
        }

        if (string.IsNullOrWhiteSpace(field.Source))
        {
            throw new InvalidOperationException($"Field '{field.Name}' must define either a source or a literal value.");
        }

        var transaction = context.Transaction;

        return field.Source.ToLowerInvariant() switch
        {
            TransactionFileFieldSources.MerchantId => context.Merchant.MerchantId,
            TransactionFileFieldSources.ContractId => context.Contract.ContractId,
            TransactionFileFieldSources.ContractIssuer => context.Contract.Issuer,
            TransactionFileFieldSources.ProductCode => GetTransaction(transaction).ProductCode,
            TransactionFileFieldSources.Description => GetTransaction(transaction).Description,
            TransactionFileFieldSources.RecipientMobileNumber => GetTransaction(transaction).RecipientMobileNumber,
            TransactionFileFieldSources.Quantity => ApplyFormat(GetTransaction(transaction).Quantity, field.Format),
            TransactionFileFieldSources.UnitAmount => ApplyFormat(GetTransaction(transaction).UnitAmount, field.Format),
            TransactionFileFieldSources.TotalAmount => ApplyFormat(GetTransaction(transaction).TotalAmount, field.Format),
            TransactionFileFieldSources.Currency => GetTransaction(transaction).Currency,
            TransactionFileFieldSources.TransactionDateUtc => ApplyDateFormat(GetTransaction(transaction).TransactionDateUtc, field.Format),
            TransactionFileFieldSources.ProcessingDateUtc => ApplyDateFormat(context.ProcessingTimestampUtc, field.Format),
            TransactionFileFieldSources.RecordCount => ApplyFormat(context.RecordCount, field.Format),
            TransactionFileFieldSources.FileTotalAmount => ApplyFormat(context.FileTotalAmount, field.Format ?? "0.00"),
            _ => throw new InvalidOperationException($"Unsupported field source '{field.Source}'.")
        };
    }

    private static GeneratedTransaction GetTransaction(GeneratedTransaction? transaction) =>
        transaction ?? throw new InvalidOperationException("The requested field requires a transaction record, but no transaction context was supplied.");

    private static object ApplyFormat<T>(T value, string? format)
        where T : IFormattable
    {
        return string.IsNullOrWhiteSpace(format)
            ? value
            : value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static object ApplyDateFormat(DateTimeOffset value, string? format) =>
        value.ToString(string.IsNullOrWhiteSpace(format) ? "yyyy-MM-dd" : format, CultureInfo.InvariantCulture);
}

public static class GeneratedFileNameFactory
{
    public static string BuildFileName(
        FileProfileOptions fileProfile,
        MerchantOptions merchant,
        ContractOptions contract,
        DateTimeOffset processingTimestampUtc)
    {
        var extension = fileProfile.FileExtension.TrimStart('.');

        if (string.IsNullOrWhiteSpace(fileProfile.FileNamePattern))
        {
            return $"{Sanitize(merchant.MerchantId)}_{Sanitize(contract.ContractId)}_{processingTimestampUtc:yyyyMMddTHHmmssZ}.{extension}";
        }

        return fileProfile.FileNamePattern
            .Replace("{merchantId}", Sanitize(merchant.MerchantId), StringComparison.OrdinalIgnoreCase)
            .Replace("{contractId}", Sanitize(contract.ContractId), StringComparison.OrdinalIgnoreCase)
            .Replace("{fileProfileId}", Sanitize(fileProfile.FileProfileId), StringComparison.OrdinalIgnoreCase)
            .Replace("{format}", Sanitize(fileProfile.Format), StringComparison.OrdinalIgnoreCase)
            .Replace("{timestampUtc}", processingTimestampUtc.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var buffer = value.ToCharArray();

        for (var index = 0; index < buffer.Length; index++)
        {
            if (invalidCharacters.Contains(buffer[index]))
            {
                buffer[index] = '_';
            }
        }

        return new string(buffer);
    }
}
