using System.Globalization;

namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public static class MerchantProcessingOptionsValidator {
    public static bool Validate(MerchantProcessingOptions options) {
        if (options.Merchants.Count == 0) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.Authentication.ClientId) || string.IsNullOrWhiteSpace(options.Authentication.ClientSecret)) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.FileProcessing.UserId) || !Guid.TryParse(options.FileProcessing.UserId, out _)) {
            return false;
        }

        if (options.FileProfiles.Count == 0) {
            return false;
        }

        if (options.TransactionGeneration.MinimumTransactionsPerContract <= 1 || options.TransactionGeneration.MaximumTransactionsPerContract < options.TransactionGeneration.MinimumTransactionsPerContract) {
            return false;
        }

        if (options.FileStatusPolling.PollIntervalSeconds <= 0) {
            return false;
        }

        if (options.ContractDefinitions.Count == 0) {
            return false;
        }

        var fileProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contractIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileProfile in options.FileProfiles) {
            if (string.IsNullOrWhiteSpace(fileProfile.FileProfileId) || string.IsNullOrWhiteSpace(fileProfile.FileProcessorFileProfileId) || !fileProfileIds.Add(fileProfile.FileProfileId) || !Guid.TryParse(fileProfile.FileProcessorFileProfileId, out _) || !FileProfileFormats.All.Contains(fileProfile.Format) || string.IsNullOrWhiteSpace(fileProfile.FileExtension) || fileProfile.Fields.Count == 0) {
                return false;
            }

            if (fileProfile.Format.Equals(FileProfileFormats.Delimited, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(fileProfile.Delimited.Delimiter)) {
                return false;
            }

            if (!AreFieldsValid(fileProfile.Fields)) {
                return false;
            }

            if (!AreFieldsValid(fileProfile.Delimited.HeaderFields) || !AreFieldsValid(fileProfile.Delimited.TrailerFields)) {
                return false;
            }
        }

        foreach (var contractDefinition in options.ContractDefinitions) {
            if (string.IsNullOrWhiteSpace(contractDefinition.ContractId) || string.IsNullOrWhiteSpace(contractDefinition.FileProfileId) || !contractIds.Add(contractDefinition.ContractId) || !fileProfileIds.Contains(contractDefinition.FileProfileId)) {
                return false;
            }
        }

        foreach (var merchant in options.Merchants) {
            var configuredTimes = merchant.RunTimesUtc.Count > 0 ? merchant.RunTimesUtc : [merchant.RunAtUtc];

            if (string.IsNullOrWhiteSpace(merchant.EstateId) || string.IsNullOrWhiteSpace(merchant.MerchantId) || !Guid.TryParse(merchant.EstateId, out _) || !Guid.TryParse(merchant.MerchantId, out _) || configuredTimes.Count == 0 || configuredTimes.Any(runTime => !TimeOnly.TryParseExact(runTime, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))) {
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