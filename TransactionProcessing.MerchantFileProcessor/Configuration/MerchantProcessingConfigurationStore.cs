using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TransactionProcessing.MerchantFileProcessor.Persistence;

namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public interface IMerchantProcessingConfigurationStore
{
    Task<MerchantProcessingConfigurationSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken);

    Task<MerchantProcessingConfigurationSnapshot> SaveJsonAsync(string configurationJson, CancellationToken cancellationToken);

    Task<MerchantProcessingConfigurationSnapshot> SaveAsync(MerchantProcessingOptions options, CancellationToken cancellationToken);
}

public sealed class MerchantProcessingConfigurationStore(
    IDbContextFactory<MerchantFileProcessorDbContext> dbContextFactory,
    IConfiguration configuration,
    IMerchantProcessingConfigurationState state,
    JsonSerializerOptions jsonSerializerOptions) : IMerchantProcessingConfigurationStore
{
    private readonly SemaphoreSlim loadLock = new(1, 1);

    public async Task<MerchantProcessingConfigurationSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken)
    {
        if (state.IsLoaded)
        {
            return new MerchantProcessingConfigurationSnapshot(state.Current, state.CurrentJson, state.UpdatedUtc);
        }

        await this.EnsureLoadedAsync(cancellationToken);
        return new MerchantProcessingConfigurationSnapshot(state.Current, state.CurrentJson, state.UpdatedUtc);
    }

    public async Task<MerchantProcessingConfigurationSnapshot> SaveJsonAsync(string configurationJson, CancellationToken cancellationToken)
    {
        var options = JsonSerializer.Deserialize<MerchantProcessingOptions>(configurationJson, jsonSerializerOptions)
            ?? throw new InvalidOperationException("The supplied configuration JSON could not be deserialised.");

        return await this.SaveAsync(options, cancellationToken);
    }

    public async Task<MerchantProcessingConfigurationSnapshot> SaveAsync(MerchantProcessingOptions options, CancellationToken cancellationToken)
    {
        if (!MerchantProcessingOptionsValidator.Validate(options))
        {
            throw new InvalidOperationException("MerchantProcessing configuration is invalid.");
        }

        var updatedUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(options, jsonSerializerOptions);
        var snapshot = new MerchantProcessingConfigurationSnapshot(options, json, updatedUtc);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await ClearConfigurationTablesAsync(dbContext, cancellationToken);
        await PersistSingletonsAsync(dbContext, options, updatedUtc, cancellationToken);
        await PersistFileProfilesAsync(dbContext, options.FileProfiles, updatedUtc, cancellationToken);
        await PersistContractsAsync(dbContext, options.ContractDefinitions, updatedUtc, cancellationToken);
        await PersistMerchantsAsync(dbContext, options.Merchants, updatedUtc, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        state.Set(snapshot);
        return snapshot;
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (state.IsLoaded)
        {
            return;
        }

        await loadLock.WaitAsync(cancellationToken);
        try
        {
            if (state.IsLoaded)
            {
                return;
            }

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var snapshot = await TryLoadFromRelationalTablesAsync(dbContext, cancellationToken)
                ?? await TryLoadFromLegacyConfigurationAsync(dbContext, cancellationToken)
                ?? BuildBootstrapSnapshot();

            if (!MerchantProcessingOptionsValidator.Validate(snapshot.Options))
            {
                throw new InvalidOperationException("No valid MerchantProcessing configuration was found in SQLite or the bootstrap configuration.");
            }

            if (!await HasNormalizedConfigurationAsync(dbContext, cancellationToken))
            {
                await this.SaveAsync(snapshot.Options, cancellationToken);
                return;
            }

            state.Set(snapshot);
        }
        finally
        {
            loadLock.Release();
        }
    }

    private MerchantProcessingConfigurationSnapshot BuildBootstrapSnapshot()
    {
        var seedOptions = configuration.GetSection(MerchantProcessingOptions.SectionName).Get<MerchantProcessingOptions>() ?? new MerchantProcessingOptions();
        var updatedUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(seedOptions, jsonSerializerOptions);
        return new MerchantProcessingConfigurationSnapshot(seedOptions, json, updatedUtc);
    }

    private async Task<MerchantProcessingConfigurationSnapshot?> TryLoadFromRelationalTablesAsync(MerchantFileProcessorDbContext dbContext, CancellationToken cancellationToken)
    {
        var snapshot = await LoadRelationalConfigurationAsync(dbContext, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(snapshot.Options, jsonSerializerOptions);
        return new MerchantProcessingConfigurationSnapshot(snapshot.Options, json, snapshot.UpdatedUtc);
    }

    private async Task<RelationalConfigurationSnapshot?> LoadRelationalConfigurationAsync(MerchantFileProcessorDbContext dbContext, CancellationToken cancellationToken)
    {
        var authentication = await dbContext.MerchantProcessingAuthenticationRecords.AsNoTracking().FirstOrDefaultAsync(record => record.Id == 1, cancellationToken);
        var fileProcessing = await dbContext.MerchantProcessingFileProcessingRecords.AsNoTracking().FirstOrDefaultAsync(record => record.Id == 1, cancellationToken);
        var transactionGeneration = await dbContext.MerchantProcessingTransactionGenerationRecords.AsNoTracking().FirstOrDefaultAsync(record => record.Id == 1, cancellationToken);
        var fileStatusPolling = await dbContext.MerchantProcessingFileStatusPollingRecords.AsNoTracking().FirstOrDefaultAsync(record => record.Id == 1, cancellationToken);
        var merchantScan = await dbContext.MerchantProcessingMerchantScanRecords.AsNoTracking().FirstOrDefaultAsync(record => record.Id == 1, cancellationToken);

        var fileProfiles = await dbContext.MerchantProcessingFileProfileRecords.AsNoTracking()
            .OrderBy(record => record.SortOrder)
            .ToListAsync(cancellationToken);

        var contractDefinitions = await dbContext.MerchantProcessingContractDefinitionRecords.AsNoTracking()
            .OrderBy(record => record.SortOrder)
            .ToListAsync(cancellationToken);

        var merchants = await dbContext.MerchantProcessingMerchantRecords.AsNoTracking()
            .OrderBy(record => record.SortOrder)
            .ToListAsync(cancellationToken);

        if (authentication is null && fileProcessing is null && transactionGeneration is null && fileStatusPolling is null && merchantScan is null &&
            fileProfiles.Count == 0 && contractDefinitions.Count == 0 && merchants.Count == 0)
        {
            return null;
        }

        if (authentication is null || fileProcessing is null || transactionGeneration is null || fileStatusPolling is null || merchantScan is null)
        {
            return null;
        }

        var fileProfileFields = await LoadFieldRecordsAsync(dbContext.MerchantProcessingFileProfileFieldRecords, cancellationToken);
        var headerFieldRecords = await LoadFieldRecordsAsync(dbContext.MerchantProcessingFileProfileHeaderFieldRecords, cancellationToken);
        var trailerFieldRecords = await LoadFieldRecordsAsync(dbContext.MerchantProcessingFileProfileTrailerFieldRecords, cancellationToken);
        var merchantRunTimes = await LoadRunTimesAsync(dbContext, cancellationToken);

        var options = new MerchantProcessingOptions
        {
            Authentication = new AuthenticationOptions
            {
                ClientId = authentication.ClientId,
                ClientSecret = authentication.ClientSecret,
                Scope = authentication.Scope,
                Audience = authentication.Audience
            },
            FileProcessing = new FileProcessingOptions
            {
                UserId = fileProcessing.UserId
            },
            TransactionGeneration = new TransactionGenerationOptions
            {
                MinimumTransactionsPerContract = transactionGeneration.MinimumTransactionsPerContract,
                MaximumTransactionsPerContract = transactionGeneration.MaximumTransactionsPerContract
            },
            FileStatusPolling = new FileStatusPollingOptions
            {
                PollIntervalSeconds = fileStatusPolling.PollIntervalSeconds
            },
            MerchantScanIntervalSeconds = merchantScan.MerchantScanIntervalSeconds
        };

        options.FileProfiles.AddRange(fileProfiles.Select(record =>
        {
            fileProfileFields.TryGetValue(record.Id, out var bodyFields);
            headerFieldRecords.TryGetValue(record.Id, out var headerFields);
            trailerFieldRecords.TryGetValue(record.Id, out var trailerFields);

            return new FileProfileOptions
            {
                FileProfileId = record.FileProfileId,
                FileProcessorFileProfileId = record.FileProcessorFileProfileId,
                Format = record.Format,
                FileExtension = record.FileExtension,
                FileNamePattern = record.FileNamePattern,
                ContentType = record.ContentType,
                Delimited = new DelimitedFileProfileOptions
                {
                    Delimiter = record.Delimiter ?? ",",
                    IncludeHeader = record.IncludeHeader,
                    HeaderFields = [.. (headerFields ?? [])],
                    TrailerFields = [.. (trailerFields ?? [])]
                },
                Json = new JsonFileProfileOptions
                {
                    WriteIndented = record.WriteIndented,
                    RootPropertyName = record.RootPropertyName
                },
                Fields = [.. (bodyFields ?? [])]
            };
        }));

        options.ContractDefinitions.AddRange(contractDefinitions.Select(record => new ContractDefinitionOptions
        {
            ContractId = record.ContractId,
            FileProfileId = record.FileProfileId
        }));

        options.Merchants.AddRange(merchants.Select(record =>
        {
            merchantRunTimes.TryGetValue(record.Id, out var runTimes);

            return new MerchantOptions
            {
                Name = record.Name,
                Enabled = record.Enabled,
                EstateId = record.EstateId,
                MerchantId = record.MerchantId,
                RunAtUtc = record.RunAtUtc,
                RunTimesUtc = [.. (runTimes ?? [])]
            };
        }));

        if (!MerchantProcessingOptionsValidator.Validate(options))
        {
            return null;
        }

        var updatedUtc = MaxUtc(
            [authentication.UpdatedUtc],
            [fileProcessing.UpdatedUtc],
            [transactionGeneration.UpdatedUtc],
            [fileStatusPolling.UpdatedUtc],
            fileProfiles.Select(record => record.UpdatedUtc),
            contractDefinitions.Select(record => record.UpdatedUtc),
            merchants.Select(record => record.UpdatedUtc));

        return new RelationalConfigurationSnapshot(options, updatedUtc);
    }

    private async Task<MerchantProcessingConfigurationSnapshot?> TryLoadFromLegacyConfigurationAsync(
        MerchantFileProcessorDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.MerchantProcessingConfigurationRecords.AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == 1, cancellationToken);

        if (record is null)
        {
            return null;
        }

        var options = JsonSerializer.Deserialize<MerchantProcessingOptions>(record.ConfigurationJson, jsonSerializerOptions)
            ?? throw new InvalidOperationException("Stored MerchantProcessing configuration could not be deserialised.");

        var updatedUtc = record.UpdatedUtc;
        var json = JsonSerializer.Serialize(options, jsonSerializerOptions);
        return new MerchantProcessingConfigurationSnapshot(options, json, updatedUtc);
    }

    private sealed record RelationalConfigurationSnapshot(MerchantProcessingOptions Options, DateTimeOffset UpdatedUtc);

    private async Task<bool> HasNormalizedConfigurationAsync(MerchantFileProcessorDbContext dbContext, CancellationToken cancellationToken)
    {
        return await dbContext.MerchantProcessingAuthenticationRecords.AnyAsync(record => record.Id == 1, cancellationToken) ||
               await dbContext.MerchantProcessingFileProcessingRecords.AnyAsync(record => record.Id == 1, cancellationToken) ||
               await dbContext.MerchantProcessingTransactionGenerationRecords.AnyAsync(record => record.Id == 1, cancellationToken) ||
               await dbContext.MerchantProcessingFileStatusPollingRecords.AnyAsync(record => record.Id == 1, cancellationToken) ||
               await dbContext.MerchantProcessingMerchantScanRecords.AnyAsync(record => record.Id == 1, cancellationToken) ||
               await dbContext.MerchantProcessingFileProfileRecords.AnyAsync(cancellationToken) ||
               await dbContext.MerchantProcessingContractDefinitionRecords.AnyAsync(cancellationToken) ||
               await dbContext.MerchantProcessingMerchantRecords.AnyAsync(cancellationToken);
    }

    private static async Task<Dictionary<int, List<FileFieldOptions>>> LoadFieldRecordsAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        where TEntity : MerchantProcessingFileProfileFieldRecordBase
    {
        var records = await query.AsNoTracking()
            .OrderBy(record => record.FileProfileRecordId)
            .ThenBy(record => record.SortOrder)
            .ToListAsync(cancellationToken);

        return records
            .GroupBy(record => record.FileProfileRecordId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(record => new FileFieldOptions
                {
                    Name = record.Name,
                    Source = record.Source ?? string.Empty,
                    Format = record.Format,
                    Value = record.Value
                }).ToList());
    }

    private static async Task<Dictionary<int, List<string>>> LoadRunTimesAsync(
        MerchantFileProcessorDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var merchantIdLookup = await dbContext.MerchantProcessingMerchantRecords.AsNoTracking()
            .Select(record => new { record.Id, record.MerchantId })
            .ToDictionaryAsync(record => record.Id, record => record.MerchantId, cancellationToken);

        var records = await dbContext.MerchantProcessingMerchantRunTimeRecords.AsNoTracking()
            .OrderBy(record => record.MerchantRecordId)
            .ThenBy(record => record.SortOrder)
            .ToListAsync(cancellationToken);

        return records
            .Where(record => merchantIdLookup.ContainsKey(record.MerchantRecordId))
            .GroupBy(record => record.MerchantRecordId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(record => record.RunTimeUtc).ToList());
    }

    private static async Task PersistSingletonsAsync(
        MerchantFileProcessorDbContext dbContext,
        MerchantProcessingOptions options,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken)
    {
        dbContext.MerchantProcessingAuthenticationRecords.Add(new MerchantProcessingAuthenticationRecord
        {
            Id = 1,
            ClientId = options.Authentication.ClientId,
            ClientSecret = options.Authentication.ClientSecret,
            Scope = options.Authentication.Scope,
            Audience = options.Authentication.Audience,
            UpdatedUtc = updatedUtc
        });

        dbContext.MerchantProcessingFileProcessingRecords.Add(new MerchantProcessingFileProcessingRecord
        {
            Id = 1,
            UserId = options.FileProcessing.UserId,
            UpdatedUtc = updatedUtc
        });

        dbContext.MerchantProcessingTransactionGenerationRecords.Add(new MerchantProcessingTransactionGenerationRecord
        {
            Id = 1,
            MinimumTransactionsPerContract = options.TransactionGeneration.MinimumTransactionsPerContract,
            MaximumTransactionsPerContract = options.TransactionGeneration.MaximumTransactionsPerContract,
            UpdatedUtc = updatedUtc
        });

        dbContext.MerchantProcessingFileStatusPollingRecords.Add(new MerchantProcessingFileStatusPollingRecord
        {
            Id = 1,
            PollIntervalSeconds = options.FileStatusPolling.PollIntervalSeconds,
            UpdatedUtc = updatedUtc
        });

        dbContext.MerchantProcessingMerchantScanRecords.Add(new MerchantProcessingMerchantScanRecord
        {
            Id = 1,
            MerchantScanIntervalSeconds = options.MerchantScanIntervalSeconds,
            UpdatedUtc = updatedUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task PersistFileProfilesAsync(
        MerchantFileProcessorDbContext dbContext,
        IReadOnlyList<FileProfileOptions> fileProfiles,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken)
    {
        var profileRecords = fileProfiles
            .Select((profile, index) => new MerchantProcessingFileProfileRecord
            {
                SortOrder = index,
                FileProfileId = profile.FileProfileId,
                FileProcessorFileProfileId = profile.FileProcessorFileProfileId,
                Format = profile.Format,
                FileExtension = profile.FileExtension,
                FileNamePattern = profile.FileNamePattern,
                ContentType = profile.ContentType,
                Delimiter = profile.Delimited.Delimiter,
                IncludeHeader = profile.Delimited.IncludeHeader,
                WriteIndented = profile.Json.WriteIndented,
                RootPropertyName = profile.Json.RootPropertyName,
                UpdatedUtc = updatedUtc
            })
            .ToArray();

        dbContext.MerchantProcessingFileProfileRecords.AddRange(profileRecords);
        await dbContext.SaveChangesAsync(cancellationToken);

        var bodyFields = new List<MerchantProcessingFileProfileFieldRecord>();
        var headerFields = new List<MerchantProcessingFileProfileHeaderFieldRecord>();
        var trailerFields = new List<MerchantProcessingFileProfileTrailerFieldRecord>();

        foreach (var profileRecord in profileRecords)
        {
            var profile = fileProfiles[profileRecord.SortOrder];

            bodyFields.AddRange(profile.Fields.Select((field, index) => new MerchantProcessingFileProfileFieldRecord
            {
                FileProfileRecordId = profileRecord.Id,
                SortOrder = index,
                Name = field.Name,
                Source = field.Source,
                Format = field.Format,
                Value = field.Value,
                UpdatedUtc = updatedUtc
            }));

            headerFields.AddRange(profile.Delimited.HeaderFields.Select((field, index) => new MerchantProcessingFileProfileHeaderFieldRecord
            {
                FileProfileRecordId = profileRecord.Id,
                SortOrder = index,
                Name = field.Name,
                Source = field.Source,
                Format = field.Format,
                Value = field.Value,
                UpdatedUtc = updatedUtc
            }));

            trailerFields.AddRange(profile.Delimited.TrailerFields.Select((field, index) => new MerchantProcessingFileProfileTrailerFieldRecord
            {
                FileProfileRecordId = profileRecord.Id,
                SortOrder = index,
                Name = field.Name,
                Source = field.Source,
                Format = field.Format,
                Value = field.Value,
                UpdatedUtc = updatedUtc
            }));
        }

        dbContext.MerchantProcessingFileProfileFieldRecords.AddRange(bodyFields);
        dbContext.MerchantProcessingFileProfileHeaderFieldRecords.AddRange(headerFields);
        dbContext.MerchantProcessingFileProfileTrailerFieldRecords.AddRange(trailerFields);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task PersistContractsAsync(
        MerchantFileProcessorDbContext dbContext,
        IReadOnlyList<ContractDefinitionOptions> contracts,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken)
    {
        var records = contracts
            .Select((contract, index) => new MerchantProcessingContractDefinitionRecord
            {
                SortOrder = index,
                ContractId = contract.ContractId,
                FileProfileId = contract.FileProfileId,
                UpdatedUtc = updatedUtc
            })
            .ToArray();

        dbContext.MerchantProcessingContractDefinitionRecords.AddRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task PersistMerchantsAsync(
        MerchantFileProcessorDbContext dbContext,
        IReadOnlyList<MerchantOptions> merchants,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken)
    {
        var merchantRecords = merchants
            .Select((merchant, index) =>
            {
                var runTimes = merchant.RunTimesUtc.Count > 0 ? merchant.RunTimesUtc : [merchant.RunAtUtc];
                return new MerchantProcessingMerchantRecord
                {
                    SortOrder = index,
                    Name = merchant.Name,
                    Enabled = merchant.Enabled,
                    EstateId = merchant.EstateId,
                    MerchantId = merchant.MerchantId,
                    RunAtUtc = runTimes.FirstOrDefault() ?? merchant.RunAtUtc,
                    UpdatedUtc = updatedUtc
                };
            })
            .ToArray();

        dbContext.MerchantProcessingMerchantRecords.AddRange(merchantRecords);
        await dbContext.SaveChangesAsync(cancellationToken);

        var runTimeRecords = new List<MerchantProcessingMerchantRunTimeRecord>();
        foreach (var merchantRecord in merchantRecords)
        {
            var merchant = merchants[merchantRecord.SortOrder];
            var runTimes = merchant.RunTimesUtc.Count > 0 ? merchant.RunTimesUtc : [merchant.RunAtUtc];

            runTimeRecords.AddRange(runTimes.Select((runTime, index) => new MerchantProcessingMerchantRunTimeRecord
            {
                MerchantRecordId = merchantRecord.Id,
                SortOrder = index,
                RunTimeUtc = runTime,
                UpdatedUtc = updatedUtc
            }));
        }

        dbContext.MerchantProcessingMerchantRunTimeRecords.AddRange(runTimeRecords);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ClearConfigurationTablesAsync(MerchantFileProcessorDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingMerchantRunTimes;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingMerchants;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingFileProfileTrailerFields;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingFileProfileHeaderFields;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingFileProfileFields;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingFileProfiles;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingContractDefinitions;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingAuthenticationRecords;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingFileProcessingRecords;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingTransactionGenerationRecords;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingFileStatusPollingRecords;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM MerchantProcessingMerchantScanRecords;", cancellationToken);
    }

    private static DateTimeOffset MaxUtc(params IEnumerable<DateTimeOffset>?[] sources)
    {
        var values = sources
            .Where(source => source is not null)
            .SelectMany(source => source!)
            .ToArray();

        return values.Length == 0 ? DateTimeOffset.UtcNow : values.Max();
    }
}
