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
        snapshot.WorkerSettings ??= new WorkerSettings();
        snapshot.WorkerSettings.ClientId = string.IsNullOrWhiteSpace(snapshot.WorkerSettings.ClientId) ? defaults.WorkerSettings.ClientId : snapshot.WorkerSettings.ClientId;
        snapshot.WorkerSettings.ClientSecret = string.IsNullOrWhiteSpace(snapshot.WorkerSettings.ClientSecret) ? defaults.WorkerSettings.ClientSecret : snapshot.WorkerSettings.ClientSecret;
        snapshot.WorkerSettings.ServiceClientId = string.IsNullOrWhiteSpace(snapshot.WorkerSettings.ServiceClientId) ? defaults.WorkerSettings.ServiceClientId : snapshot.WorkerSettings.ServiceClientId;
        snapshot.WorkerSettings.ServiceClientSecret = string.IsNullOrWhiteSpace(snapshot.WorkerSettings.ServiceClientSecret) ? defaults.WorkerSettings.ServiceClientSecret : snapshot.WorkerSettings.ServiceClientSecret;
        if (snapshot.WorkerSettings.Merchants is null || snapshot.WorkerSettings.Merchants.Count == 0)
        {
            snapshot.WorkerSettings.Merchants = defaults.WorkerSettings.Merchants;
        }

        snapshot.ApiConfiguration ??= new ApiConfigurationSettings();
        snapshot.ApiConfiguration.SecurityService = string.IsNullOrWhiteSpace(snapshot.ApiConfiguration.SecurityService) ? defaults.ApiConfiguration.SecurityService : snapshot.ApiConfiguration.SecurityService;
        snapshot.ApiConfiguration.TransactionProcessorACL = string.IsNullOrWhiteSpace(snapshot.ApiConfiguration.TransactionProcessorACL) ? defaults.ApiConfiguration.TransactionProcessorACL : snapshot.ApiConfiguration.TransactionProcessorACL;
        snapshot.ApiConfiguration.TransactionProcessorApi = string.IsNullOrWhiteSpace(snapshot.ApiConfiguration.TransactionProcessorApi) ? defaults.ApiConfiguration.TransactionProcessorApi : snapshot.ApiConfiguration.TransactionProcessorApi;
        snapshot.ApiConfiguration.TestHost = string.IsNullOrWhiteSpace(snapshot.ApiConfiguration.TestHost) ? defaults.ApiConfiguration.TestHost : snapshot.ApiConfiguration.TestHost;

        snapshot.ConnectionStrings ??= new ConnectionStringsSettings();
        snapshot.ConnectionStrings.MerchantDb = string.IsNullOrWhiteSpace(snapshot.ConnectionStrings.MerchantDb) ? defaults.ConnectionStrings.MerchantDb : snapshot.ConnectionStrings.MerchantDb;
        snapshot.ConnectionStrings.SettingsDb = string.IsNullOrWhiteSpace(snapshot.ConnectionStrings.SettingsDb) ? defaults.ConnectionStrings.SettingsDb : snapshot.ConnectionStrings.SettingsDb;
        snapshot.EnsureDefaults();
        return snapshot;
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
