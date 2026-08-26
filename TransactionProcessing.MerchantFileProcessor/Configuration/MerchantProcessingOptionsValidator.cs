using System.Globalization;

namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public static class MerchantProcessingOptionsValidator {
    public static bool Validate(MerchantProcessingOptions options) {
        return HasValidAuthentication(options)
            && HasValidFileProcessing(options)
            && HasValidTransactionGeneration(options)
            && HasValidPolling(options)
            && HasValidMerchantScan(options)
            && HasValidFileProfiles(options, out var fileProfileIds)
            && HasValidContracts(options, fileProfileIds)
            && HasValidMerchants(options);
    }

    private static bool HasValidAuthentication(MerchantProcessingOptions options) =>
        !string.IsNullOrWhiteSpace(options.Authentication.ClientId) && !string.IsNullOrWhiteSpace(options.Authentication.ClientSecret);

    private static bool HasValidFileProcessing(MerchantProcessingOptions options) =>
        !string.IsNullOrWhiteSpace(options.FileProcessing.UserId) && Guid.TryParse(options.FileProcessing.UserId, out _);

    private static bool HasValidTransactionGeneration(MerchantProcessingOptions options) =>
        options.TransactionGeneration.MinimumTransactionsPerContract > 1 &&
        options.TransactionGeneration.MaximumTransactionsPerContract >= options.TransactionGeneration.MinimumTransactionsPerContract;

    private static bool HasValidPolling(MerchantProcessingOptions options) =>
        options.FileStatusPolling.PollIntervalSeconds > 0;

    private static bool HasValidMerchantScan(MerchantProcessingOptions options) =>
        options.MerchantScanIntervalSeconds > 0;

    private static bool HasValidFileProfiles(MerchantProcessingOptions options, out HashSet<string> fileProfileIds)
    {
        fileProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options.FileProfiles.Count == 0)
        {
            return false;
        }

        foreach (var fileProfile in options.FileProfiles)
        {
            if (!IsValidFileProfile(fileProfile, fileProfileIds))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidFileProfile(FileProfileOptions fileProfile, ISet<string> fileProfileIds)
    {
        if (string.IsNullOrWhiteSpace(fileProfile.FileProfileId) ||
            string.IsNullOrWhiteSpace(fileProfile.FileProcessorFileProfileId) ||
            !fileProfileIds.Add(fileProfile.FileProfileId) ||
            !Guid.TryParse(fileProfile.FileProcessorFileProfileId, out _) ||
            !FileProfileFormats.All.Contains(fileProfile.Format) ||
            string.IsNullOrWhiteSpace(fileProfile.FileExtension) ||
            fileProfile.Fields.Count == 0)
        {
            return false;
        }

        if (fileProfile.Format.Equals(FileProfileFormats.Delimited, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(fileProfile.Delimited.Delimiter))
        {
            return false;
        }

        return AreFieldsValid(fileProfile.Fields) &&
               AreFieldsValid(fileProfile.Delimited.HeaderFields) &&
               AreFieldsValid(fileProfile.Delimited.TrailerFields);
    }

    private static bool HasValidContracts(MerchantProcessingOptions options, ISet<string> fileProfileIds)
    {
        if (options.ContractDefinitions.Count == 0)
        {
            return false;
        }

        var contractIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contractDefinition in options.ContractDefinitions)
        {
            if (string.IsNullOrWhiteSpace(contractDefinition.ContractId) ||
                string.IsNullOrWhiteSpace(contractDefinition.FileProfileId) ||
                !contractIds.Add(contractDefinition.ContractId) ||
                !fileProfileIds.Contains(contractDefinition.FileProfileId))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidMerchants(MerchantProcessingOptions options)
    {
        foreach (var merchant in options.Merchants)
        {
            var configuredTimes = merchant.RunTimesUtc.Count > 0 ? merchant.RunTimesUtc : [merchant.RunAtUtc];

            if (string.IsNullOrWhiteSpace(merchant.EstateId) ||
                string.IsNullOrWhiteSpace(merchant.MerchantId) ||
                !Guid.TryParse(merchant.EstateId, out _) ||
                !Guid.TryParse(merchant.MerchantId, out _) ||
                configuredTimes.Count == 0 ||
                configuredTimes.Any(runTime => !TimeOnly.TryParseExact(runTime, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreFieldsValid(IEnumerable<FileFieldOptions> fields) {
        foreach (var field in fields) {
            var hasLiteralValue = !string.IsNullOrWhiteSpace(field.Value);
            var hasSource = !string.IsNullOrWhiteSpace(field.Source);

            if (string.IsNullOrWhiteSpace(field.Name) || (!hasLiteralValue && !hasSource) || (hasSource && !TransactionFileFieldSources.All.Contains(field.Source))) {
                return false;
            }
        }

        return true;
    }
}
