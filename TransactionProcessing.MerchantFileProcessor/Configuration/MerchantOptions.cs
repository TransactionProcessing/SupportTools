using System.Globalization;

namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class MerchantOptions {
    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public string EstateId { get; init; } = string.Empty;

    public string MerchantId { get; init; } = string.Empty;

    public string RunAtUtc { get; init; } = "02:00:00";

    public List<string> RunTimesUtc { get; init; } = [];

    public IReadOnlyList<TimeOnly> GetDailyRunTimesUtc() {
        var configuredTimes = this.RunTimesUtc.Count > 0 ? this.RunTimesUtc : [this.RunAtUtc];

        return configuredTimes.Select(runTime => TimeOnly.ParseExact(runTime, "HH:mm:ss", CultureInfo.InvariantCulture)).Distinct().OrderBy(runTime => runTime).ToArray();
    }

    public Guid GetEstateGuid() => Guid.Parse(this.EstateId);

    public Guid GetMerchantGuid() => Guid.Parse(this.MerchantId);
}