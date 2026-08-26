using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using Microsoft.Extensions.Primitives;

namespace TransactionProcessing.MerchantFileProcessor.Reporting;

public static class ConfigurationManagementEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapConfigurationManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/configuration/runtime", HandleRuntimeConfigurationAsync);
        endpoints.MapPost("/api/configuration/merchants", HandleMerchantConfigurationAsync);
        endpoints.MapPost("/api/configuration/contracts", HandleContractConfigurationAsync);
        endpoints.MapPost("/api/configuration/file-profiles", HandleFileProfileConfigurationAsync);
        endpoints.MapPost("/api/configuration/file-profiles/{fileProfileId}/fields/{section}", HandleFileProfileFieldConfigurationAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleRuntimeConfigurationAsync(
        HttpRequest request,
        IMerchantProcessingConfigurationStore configurationStore,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var returnUrl = GetReturnUrl(form, "/ops/config/runtime");
        var snapshot = await configurationStore.GetCurrentSnapshotAsync(cancellationToken);
        var template = Clone(snapshot.Options);
        var options = new MerchantProcessingOptions
        {
            Authentication = new AuthenticationOptions
            {
                ClientId = form["ClientId"].ToString(),
                ClientSecret = form["ClientSecret"].ToString(),
                Scope = GetOptional(form, "Scope"),
                Audience = GetOptional(form, "Audience")
            },
            FileProcessing = new FileProcessingOptions { UserId = form["UserId"].ToString() },
            TransactionGeneration = new TransactionGenerationOptions
            {
                MinimumTransactionsPerContract = ParseInt(form["MinimumTransactionsPerContract"], 5),
                MaximumTransactionsPerContract = ParseInt(form["MaximumTransactionsPerContract"], 25)
            },
            FileStatusPolling = new FileStatusPollingOptions { PollIntervalSeconds = ParseInt(form["PollIntervalSeconds"], 30) },
            MerchantScanIntervalSeconds = ParseInt(form["MerchantScanIntervalSeconds"], 5),
            ContractDefinitions = template.ContractDefinitions,
            FileProfiles = template.FileProfiles,
            Merchants = template.Merchants
        };

        return await SaveWithRedirectAsync(configurationStore, options, returnUrl, cancellationToken);
    }

    private static async Task<IResult> HandleMerchantConfigurationAsync(
        HttpRequest request,
        IMerchantProcessingConfigurationStore configurationStore,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var returnUrl = GetReturnUrl(form, "/ops/config/merchants");
        var snapshot = await configurationStore.GetCurrentSnapshotAsync(cancellationToken);
        var options = Clone(snapshot.Options);
        var originalMerchantId = GetOptional(form, "OriginalMerchantId");
        var merchantId = GetRequired(form, "MerchantId");
        var merchantIndex = options.Merchants.FindIndex(entry =>
            string.Equals(entry.MerchantId, originalMerchantId ?? merchantId, StringComparison.OrdinalIgnoreCase));

        if (ParseBool(form["Delete"]))
        {
            if (merchantIndex < 0)
            {
                return Results.Redirect($"{returnUrl}?error={Uri.EscapeDataString("Merchant not found.")}");
            }

            options.Merchants.RemoveAt(merchantIndex);
            return await SaveWithRedirectAsync(configurationStore, options, "/ops/config/merchants", cancellationToken, "removed=1");
        }

        var merchant = new MerchantOptions
        {
            Name = GetRequired(form, "Name"),
            Enabled = ParseBool(form["Enabled"]),
            EstateId = GetRequired(form, "EstateId"),
            MerchantId = merchantId,
            RunAtUtc = GetOptional(form, "RunAtUtc") ?? "02:00:00",
            RunTimesUtc = ParseRunTimes(GetOptional(form, "RunTimesUtc"))
        };

        if (merchantIndex >= 0)
        {
            options.Merchants[merchantIndex] = merchant;
        }
        else
        {
            options.Merchants.Add(merchant);
        }

        return await SaveWithRedirectAsync(configurationStore, options, returnUrl, cancellationToken);
    }

    private static async Task<IResult> HandleContractConfigurationAsync(
        HttpRequest request,
        IMerchantProcessingConfigurationStore configurationStore,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var returnUrl = GetReturnUrl(form, "/ops/config/contracts");
        var snapshot = await configurationStore.GetCurrentSnapshotAsync(cancellationToken);
        var options = Clone(snapshot.Options);
        var originalContractId = GetOptional(form, "OriginalContractId");
        var contractId = GetRequired(form, "ContractId");
        var contractIndex = options.ContractDefinitions.FindIndex(entry =>
            string.Equals(entry.ContractId, originalContractId ?? contractId, StringComparison.OrdinalIgnoreCase));

        var contract = new ContractDefinitionOptions
        {
            ContractId = contractId,
            FileProfileId = GetRequired(form, "FileProfileId")
        };

        if (contractIndex >= 0)
        {
            options.ContractDefinitions[contractIndex] = contract;
        }
        else
        {
            options.ContractDefinitions.Add(contract);
        }

        return await SaveWithRedirectAsync(configurationStore, options, returnUrl, cancellationToken);
    }

    private static async Task<IResult> HandleFileProfileConfigurationAsync(
        HttpRequest request,
        IMerchantProcessingConfigurationStore configurationStore,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var returnUrl = GetReturnUrl(form, "/ops/config/file-profiles");
        var snapshot = await configurationStore.GetCurrentSnapshotAsync(cancellationToken);
        var options = Clone(snapshot.Options);
        var originalFileProfileId = GetOptional(form, "OriginalFileProfileId");
        var fileProfileId = GetRequired(form, "FileProfileId");
        var profileIndex = options.FileProfiles.FindIndex(entry =>
            string.Equals(entry.FileProfileId, originalFileProfileId ?? fileProfileId, StringComparison.OrdinalIgnoreCase));

        var existingProfile = profileIndex >= 0 ? options.FileProfiles[profileIndex] : new FileProfileOptions();
        var profile = new FileProfileOptions
        {
            FileProfileId = fileProfileId,
            FileProcessorFileProfileId = GetRequired(form, "FileProcessorFileProfileId"),
            Format = GetRequired(form, "Format"),
            FileExtension = GetRequired(form, "FileExtension"),
            FileNamePattern = GetOptional(form, "FileNamePattern"),
            ContentType = GetOptional(form, "ContentType"),
            Delimited = new DelimitedFileProfileOptions
            {
                Delimiter = GetOptional(form, "Delimiter") ?? ",",
                IncludeHeader = ParseBool(form["IncludeHeader"]),
                HeaderFields = existingProfile.Delimited.HeaderFields,
                TrailerFields = existingProfile.Delimited.TrailerFields
            },
            Json = new JsonFileProfileOptions
            {
                WriteIndented = ParseBool(form["WriteIndented"]),
                RootPropertyName = GetOptional(form, "RootPropertyName")
            },
            Fields = existingProfile.Fields
        };

        if (profileIndex >= 0)
        {
            options.FileProfiles[profileIndex] = profile;
        }
        else
        {
            options.FileProfiles.Add(profile);
        }

        return await SaveWithRedirectAsync(configurationStore, options, returnUrl, cancellationToken);
    }

    private static async Task<IResult> HandleFileProfileFieldConfigurationAsync(
        string fileProfileId,
        string section,
        HttpRequest request,
        IMerchantProcessingConfigurationStore configurationStore,
        JsonSerializerOptions jsonSerializerOptions,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var returnUrl = GetReturnUrl(form, $"/ops/config/file-profiles/{Uri.EscapeDataString(fileProfileId)}");
        var snapshot = await configurationStore.GetCurrentSnapshotAsync(cancellationToken);
        var options = Clone(snapshot.Options);
        var profileIndex = options.FileProfiles.FindIndex(entry => entry.FileProfileId.Equals(fileProfileId, StringComparison.OrdinalIgnoreCase));

        if (profileIndex < 0)
        {
            return Results.Redirect($"{returnUrl}?error={Uri.EscapeDataString("File profile not found.")}");
        }

        var profile = options.FileProfiles[profileIndex];
        var fields = GetFieldList(profile, section);
        var originalSortOrder = ParseNullableInt(form["OriginalSortOrder"]);

        if (ParseBool(form["Delete"]))
        {
            if (originalSortOrder.HasValue && originalSortOrder.Value >= 0 && originalSortOrder.Value < fields.Count)
            {
                fields.RemoveAt(originalSortOrder.Value);
            }
        }
        else
        {
            var updatedField = new FileFieldOptions
            {
                Name = GetRequired(form, "Name"),
                Source = GetOptional(form, "Source") ?? string.Empty,
                Format = GetOptional(form, "Format"),
                Value = GetOptional(form, "Value")
            };

            if (originalSortOrder.HasValue && originalSortOrder.Value >= 0 && originalSortOrder.Value < fields.Count)
            {
                fields[originalSortOrder.Value] = updatedField;
            }
            else
            {
                fields.Add(updatedField);
            }
        }

        return await SaveWithRedirectAsync(configurationStore, options, returnUrl, cancellationToken);
    }

    private static async Task<IResult> SaveWithRedirectAsync(
        IMerchantProcessingConfigurationStore configurationStore,
        MerchantProcessingOptions options,
        string returnUrl,
        CancellationToken cancellationToken,
        string? queryString = null)
    {
        try
        {
            await configurationStore.SaveAsync(options, cancellationToken);
            var suffix = string.IsNullOrWhiteSpace(queryString) ? "saved=1" : queryString;
            return Results.Redirect($"{returnUrl}?{suffix}");
        }
        catch (Exception ex)
        {
            return Results.Redirect($"{returnUrl}?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    private static List<FileFieldOptions> GetFieldList(FileProfileOptions profile, string section)
    {
        return section.ToLowerInvariant() switch
        {
            "body" or "fields" => profile.Fields,
            "header" => profile.Delimited.HeaderFields,
            "trailer" => profile.Delimited.TrailerFields,
            _ => throw new InvalidOperationException("Unknown file profile field section.")
        };
    }

    private static MerchantProcessingOptions Clone(MerchantProcessingOptions options) =>
        new()
        {
            Authentication = new AuthenticationOptions
            {
                ClientId = options.Authentication.ClientId,
                ClientSecret = options.Authentication.ClientSecret,
                Scope = options.Authentication.Scope,
                Audience = options.Authentication.Audience
            },
            FileProcessing = new FileProcessingOptions
            {
                UserId = options.FileProcessing.UserId
            },
            TransactionGeneration = new TransactionGenerationOptions
            {
                MinimumTransactionsPerContract = options.TransactionGeneration.MinimumTransactionsPerContract,
                MaximumTransactionsPerContract = options.TransactionGeneration.MaximumTransactionsPerContract
            },
            FileStatusPolling = new FileStatusPollingOptions
            {
                PollIntervalSeconds = options.FileStatusPolling.PollIntervalSeconds
            },
            MerchantScanIntervalSeconds = options.MerchantScanIntervalSeconds,
            ContractDefinitions = options.ContractDefinitions
                .Select(contract => new ContractDefinitionOptions
                {
                    ContractId = contract.ContractId,
                    FileProfileId = contract.FileProfileId
                })
                .ToList(),
            FileProfiles = options.FileProfiles
                .Select(profile => new FileProfileOptions
                {
                    FileProfileId = profile.FileProfileId,
                    FileProcessorFileProfileId = profile.FileProcessorFileProfileId,
                    Format = profile.Format,
                    FileExtension = profile.FileExtension,
                    FileNamePattern = profile.FileNamePattern,
                    ContentType = profile.ContentType,
                    Delimited = new DelimitedFileProfileOptions
                    {
                        Delimiter = profile.Delimited.Delimiter,
                        IncludeHeader = profile.Delimited.IncludeHeader,
                        HeaderFields = profile.Delimited.HeaderFields
                            .Select(field => new FileFieldOptions
                            {
                                Name = field.Name,
                                Source = field.Source,
                                Format = field.Format,
                                Value = field.Value
                            })
                            .ToList(),
                        TrailerFields = profile.Delimited.TrailerFields
                            .Select(field => new FileFieldOptions
                            {
                                Name = field.Name,
                                Source = field.Source,
                                Format = field.Format,
                                Value = field.Value
                            })
                            .ToList()
                    },
                    Json = new JsonFileProfileOptions
                    {
                        WriteIndented = profile.Json.WriteIndented,
                        RootPropertyName = profile.Json.RootPropertyName
                    },
                    Fields = profile.Fields
                        .Select(field => new FileFieldOptions
                        {
                            Name = field.Name,
                            Source = field.Source,
                            Format = field.Format,
                            Value = field.Value
                        })
                        .ToList()
                })
                .ToList(),
            Merchants = options.Merchants
                .Select(merchant => new MerchantOptions
                {
                    Name = merchant.Name,
                    Enabled = merchant.Enabled,
                    EstateId = merchant.EstateId,
                    MerchantId = merchant.MerchantId,
                    RunAtUtc = merchant.RunAtUtc,
                    RunTimesUtc = merchant.RunTimesUtc.ToList()
                })
                .ToList()
        };

    private static string GetReturnUrl(IFormCollection form, string defaultValue) =>
        string.IsNullOrWhiteSpace(form["ReturnUrl"]) ? defaultValue : form["ReturnUrl"].ToString();

    private static string GetRequired(IFormCollection form, string key) =>
        form[key].ToString().Trim();

    private static string? GetOptional(IFormCollection form, string key)
    {
        var value = form[key].ToString().Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool ParseBool(StringValues value) =>
        value.Count > 0 && bool.TryParse(value.ToString(), out var parsed) && parsed;

    private static int ParseInt(StringValues value, int defaultValue) =>
        int.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;

    private static int? ParseNullableInt(StringValues value) =>
        int.TryParse(value.ToString(), out var parsed) ? parsed : null;

    private static List<string> ParseRunTimes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
