using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TransactionProcessing.MerchantPos.Runtime;

namespace TransactionProcessing.MerchantPos.Persistence;

public sealed class MerchantPosSettingsStore
{
    private const int SingletonRowId = 1;
    private readonly IConfiguration _configuration;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger<MerchantPosSettingsStore> _logger;
    private MerchantPosSettingsSnapshot _current = new();

    public MerchantPosSettingsStore(
        IConfiguration configuration,
        JsonSerializerOptions serializerOptions,
        ILogger<MerchantPosSettingsStore> logger)
    {
        _configuration = configuration;
        _serializerOptions = serializerOptions;
        _logger = logger;
    }

    public MerchantPosSettingsSnapshot Current => _current;

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        var defaults = MerchantPosSettingsSnapshot.FromConfiguration(_configuration);
        defaults.EnsureDefaults();

        await using var db = CreateContext(defaults.ConnectionStrings.SettingsDb);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var record = await db.Settings.SingleOrDefaultAsync(x => x.Id == SingletonRowId, cancellationToken);
        if (record is null || string.IsNullOrWhiteSpace(record.Json))
        {
            _current = defaults;
            await SaveAsync(defaults, cancellationToken);
            return;
        }

        var loaded = JsonSerializer.Deserialize<MerchantPosSettingsSnapshot>(record.Json, _serializerOptions);
        _current = loaded ?? defaults;
        _current = MergeWithDefaults(_current, defaults);
        _current.ConnectionStrings.MerchantDb = NormalizePath(_current.ConnectionStrings.MerchantDb);
        _current.ConnectionStrings.SettingsDb = NormalizePath(_current.ConnectionStrings.SettingsDb);
        _logger.LogInformation("Loaded merchant POS settings from {Path}", ResolvePath(_current.ConnectionStrings.SettingsDb));
    }

    public async Task SaveAsync(MerchantPosSettingsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        snapshot.EnsureDefaults();
        snapshot.ConnectionStrings.MerchantDb = NormalizePath(snapshot.ConnectionStrings.MerchantDb);
        snapshot.ConnectionStrings.SettingsDb = NormalizePath(snapshot.ConnectionStrings.SettingsDb);
        _current = snapshot;

        var settingsDbPath = ResolvePath(snapshot.ConnectionStrings.SettingsDb);
        await using var db = CreateContext(settingsDbPath);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var json = JsonSerializer.Serialize(snapshot, _serializerOptions);
        var record = await db.Settings.SingleOrDefaultAsync(x => x.Id == SingletonRowId, cancellationToken);
        if (record is null)
        {
            record = new MerchantPosSettingsRecord { Id = SingletonRowId };
            db.Settings.Add(record);
        }

        record.Json = json;
        record.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Saved merchant POS settings to {Path}", settingsDbPath);
    }

    public string ResolvePath(string path)
    {
        path = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransactionProcessing", "MerchantPos", "merchant-pos-settings.db");
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransactionProcessing", "MerchantPos", path));
    }

    public static string BuildSqliteConnectionString(string path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : $"Data Source={NormalizePath(path)}";

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        const string prefix = "Data Source=";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path[prefix.Length..].Trim()
            : path.Trim();
    }

    private static MerchantPosSettingsSnapshot MergeWithDefaults(MerchantPosSettingsSnapshot snapshot, MerchantPosSettingsSnapshot defaults)
    {
        ApplyWorkerDefaults(snapshot.WorkerSettings ??= new WorkerSettings(), defaults.WorkerSettings);
        ApplyApiDefaults(snapshot.ApiConfiguration ??= new ApiConfigurationSettings(), defaults.ApiConfiguration);
        ApplyConnectionStringDefaults(snapshot.ConnectionStrings ??= new ConnectionStringsSettings(), defaults.ConnectionStrings);
        snapshot.EnsureDefaults();
        return snapshot;
    }

    private static void ApplyWorkerDefaults(WorkerSettings current, WorkerSettings defaults)
    {
        current.ClientId = string.IsNullOrWhiteSpace(current.ClientId) ? defaults.ClientId : current.ClientId;
        current.ClientSecret = string.IsNullOrWhiteSpace(current.ClientSecret) ? defaults.ClientSecret : current.ClientSecret;
        current.ServiceClientId = string.IsNullOrWhiteSpace(current.ServiceClientId) ? defaults.ServiceClientId : current.ServiceClientId;
        current.ServiceClientSecret = string.IsNullOrWhiteSpace(current.ServiceClientSecret) ? defaults.ServiceClientSecret : current.ServiceClientSecret;
        current.MerchantScanIntervalSeconds = current.MerchantScanIntervalSeconds <= 0
            ? defaults.MerchantScanIntervalSeconds
            : current.MerchantScanIntervalSeconds;
        if (current.Merchants is null || current.Merchants.Count == 0)
        {
            current.Merchants = defaults.Merchants;
        }
    }

    private static void ApplyApiDefaults(ApiConfigurationSettings current, ApiConfigurationSettings defaults)
    {
        current.SecurityService = string.IsNullOrWhiteSpace(current.SecurityService) ? defaults.SecurityService : current.SecurityService;
        current.TransactionProcessorACL = string.IsNullOrWhiteSpace(current.TransactionProcessorACL) ? defaults.TransactionProcessorACL : current.TransactionProcessorACL;
        current.TransactionProcessorApi = string.IsNullOrWhiteSpace(current.TransactionProcessorApi) ? defaults.TransactionProcessorApi : current.TransactionProcessorApi;
        current.TestHost = string.IsNullOrWhiteSpace(current.TestHost) ? defaults.TestHost : current.TestHost;
    }

    private static void ApplyConnectionStringDefaults(ConnectionStringsSettings current, ConnectionStringsSettings defaults)
    {
        current.MerchantDb = string.IsNullOrWhiteSpace(current.MerchantDb) ? defaults.MerchantDb : current.MerchantDb;
        current.SettingsDb = string.IsNullOrWhiteSpace(current.SettingsDb) ? defaults.SettingsDb : current.SettingsDb;
    }

    private MerchantPosSettingsDbContext CreateContext(string settingsDbPath)
    {
        var fullPath = ResolvePath(settingsDbPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new DbContextOptionsBuilder<MerchantPosSettingsDbContext>()
            .UseSqlite($"Data Source={fullPath}")
            .Options;

        return new MerchantPosSettingsDbContext(options);
    }
}
