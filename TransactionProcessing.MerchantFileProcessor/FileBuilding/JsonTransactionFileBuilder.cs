using System.Text.Json;
using TransactionProcessing.MerchantFileProcessor.Configuration;

namespace TransactionProcessing.MerchantFileProcessor.FileBuilding;

public sealed class JsonTransactionFileBuilder : ITransactionFileBuilder
{
    public string Format => FileProfileFormats.Json;

    public GeneratedFile Build(
        MerchantOptions merchant,
        ContractOptions contract,
        FileProfileOptions fileProfile,
        IReadOnlyList<GeneratedTransaction> transactions,
        DateTimeOffset processingTimestampUtc)
    {
        var fileTotalAmount = transactions.Sum(transaction => transaction.TotalAmount);
        var mappedRecords = transactions
            .Select(transaction =>
            {
                var context = new TransactionFileContext(
                    merchant,
                    contract,
                    transaction,
                    processingTimestampUtc,
                    transactions.Count,
                    fileTotalAmount);

                return fileProfile.Fields.ToDictionary(
                    field => field.Name,
                    field => TransactionFileFieldResolver.GetValue(field, context));
            })
            .ToArray();

        object payload = string.IsNullOrWhiteSpace(fileProfile.Json.RootPropertyName)
            ? mappedRecords
            : new Dictionary<string, object?>
            {
                [fileProfile.Json.RootPropertyName] = mappedRecords
            };

        var content = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = fileProfile.Json.WriteIndented
            });

        return new GeneratedFile(
            GeneratedFileNameFactory.BuildFileName(fileProfile, merchant, contract, processingTimestampUtc),
            content,
            string.IsNullOrWhiteSpace(fileProfile.ContentType) ? "application/json" : fileProfile.ContentType,
            transactions.Count,
            transactions.Sum(transaction => transaction.TotalAmount),
            fileProfile.FileProfileId,
            fileProfile.Format,
            transactions);
    }
}
