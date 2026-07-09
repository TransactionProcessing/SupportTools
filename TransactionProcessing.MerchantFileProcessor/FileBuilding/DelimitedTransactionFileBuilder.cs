using System.Text;
using TransactionProcessing.MerchantFileProcessor.Configuration;

namespace TransactionProcessing.MerchantFileProcessor.FileBuilding;

public sealed class DelimitedTransactionFileBuilder : ITransactionFileBuilder {
    public string Format => FileProfileFormats.Delimited;

    public GeneratedFile Build(MerchantOptions merchant,
                               ContractOptions contract,
                               FileProfileOptions fileProfile,
                               IReadOnlyList<GeneratedTransaction> transactions,
                               DateTimeOffset processingTimestampUtc) {
        string delimiter = NormalizeDelimiter(fileProfile.Delimited.Delimiter);
        List<string> lines = new List<string>();
        decimal fileTotalAmount = transactions.Sum(transaction => transaction.TotalAmount);

        if (fileProfile.Delimited.HeaderFields.Count > 0) {
            TransactionFileContext headerContext = new TransactionFileContext(merchant, contract, null, processingTimestampUtc, transactions.Count, fileTotalAmount);

            lines.Add(string.Join(delimiter, fileProfile.Delimited.HeaderFields.Select(field => Escape(TransactionFileFieldResolver.GetTextValue(field, headerContext), delimiter))));
        }
        else if (fileProfile.Delimited.IncludeHeader) {
            lines.Add(string.Join(delimiter, fileProfile.Fields.Select(field => Escape(field.Name, delimiter))));
        }

        foreach (GeneratedTransaction transaction in transactions) {
            TransactionFileContext detailContext = new TransactionFileContext(merchant, contract, transaction, processingTimestampUtc, transactions.Count, fileTotalAmount);

            lines.Add(string.Join(delimiter, fileProfile.Fields.Select(field => Escape(TransactionFileFieldResolver.GetTextValue(field, detailContext), delimiter))));
        }

        if (fileProfile.Delimited.TrailerFields.Count > 0) {
            TransactionFileContext trailerContext = new TransactionFileContext(merchant, contract, null, processingTimestampUtc, transactions.Count, fileTotalAmount);

            lines.Add(string.Join(delimiter, fileProfile.Delimited.TrailerFields.Select(field => Escape(TransactionFileFieldResolver.GetTextValue(field, trailerContext), delimiter))));
        }

        return new GeneratedFile(GeneratedFileNameFactory.BuildFileName(fileProfile, merchant, contract, processingTimestampUtc), Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines)), string.IsNullOrWhiteSpace(fileProfile.ContentType) ? "text/plain" : fileProfile.ContentType, transactions.Count, fileTotalAmount, fileProfile.FileProfileId, fileProfile.Format, transactions);
    }

    private static string NormalizeDelimiter(string delimiter) => delimiter.Replace("\\t", "\t", StringComparison.Ordinal).Replace("\\r", "\r", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal);

    private static string Escape(string value,
                                 string delimiter) {
        bool shouldQuote = value.Contains('"') || value.Contains('\r') || value.Contains('\n') || value.Contains(delimiter, StringComparison.Ordinal);
        string escapedValue = value.Replace("\"", "\"\"", StringComparison.Ordinal);

        return shouldQuote ? $"\"{escapedValue}\"" : escapedValue;
    }
}
