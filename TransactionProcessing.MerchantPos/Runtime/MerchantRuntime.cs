using MerchantPos.EF.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using Shared.Logger;
using Shared.Results;
using SimpleResults;
using System.Threading;
using MerchantPos.EF.Models;
using TransactionProcessing.MerchantPos.Runtime;

public class MerchantRuntime
{
    private readonly IApiClient ApiClient;
    private readonly ISecurityServiceClient SecurityServiceClient;
    private readonly IEfRepository Repository;
    private readonly MerchantMetrics Metrics;
    private readonly Random _rng = new();

    public MerchantRuntime(IApiClient apiClient,
                           ISecurityServiceClient securityServiceClient,
                           IEfRepository repository,
                           MerchantMetrics metrics)
    {
        ApiClient = apiClient;
        this.SecurityServiceClient = securityServiceClient;
        Repository = repository;
        this.Metrics = metrics;
    }

    private TokenResponse CurrentServiceToken;
    private TokenResponse CurrentUserToken;
    private (String clientId, String clientSecret) ServiceClientCredentials;
    private (String clientId, String clientSecret) PosClientCredentials;
    public async Task RunAsync((String clientId, String clientSecret) serviceClient, (String clientId, String clientSecret) posClient , MerchantConfig config, CancellationToken cancellationToken)
    {
        Logger.LogInformation($"MerchantRuntime started for {config.MerchantName}");
        this.ServiceClientCredentials = serviceClient;
        this.PosClientCredentials = posClient;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Get the service client token here (can manage the expiry/caching at this level)
                //Result<TokenResponse> serviceToken = await this.GetToken(this.CurrentServiceToken, serviceClient, cancellationToken);
                //this.CurrentServiceToken = serviceToken.Data;

                await StartupSequence(config, cancellationToken);
                await RunMainLoop( config, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Runtime crashed for merchant {config.MerchantName}. Restarting in 5s...", ex);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task<Result> GetServiceToken(CancellationToken cancellationToken)
    {
        if (this.CurrentServiceToken == null)
        {
            Result<TokenResponse> tokenResult = await this.SecurityServiceClient.GetToken(this.ServiceClientCredentials.clientId, this.ServiceClientCredentials.clientSecret, cancellationToken);
            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            TokenResponse token = tokenResult.Data;
            Logger.LogDebug($"Token is {token.AccessToken}");
            this.CurrentServiceToken = token;
            return Result.Success();
        }

        if (this.CurrentServiceToken.Expires.UtcDateTime.Subtract(DateTime.UtcNow) < TimeSpan.FromMinutes(2))
        {
            Logger.LogDebug($"Token is about to expire at {this.CurrentServiceToken.Expires.DateTime:O}");
            Result<TokenResponse> tokenResult = await this.SecurityServiceClient.GetToken(this.ServiceClientCredentials.clientId, this.ServiceClientCredentials.clientSecret, cancellationToken);
            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            TokenResponse token = tokenResult.Data;
            Logger.LogDebug($"Token is {token.AccessToken}");
            this.CurrentServiceToken = token;
            return Result.Success();
        }
        
        return Result.Success();
    }

    private async Task<Result> GetUserToken(MerchantConfig config,
                                        CancellationToken cancellationToken)
    {
        if (this.CurrentUserToken == null)
        {
            Result<TokenResponse> tokenResult = await this.SecurityServiceClient.GetToken(config.Username, config.Password, this.PosClientCredentials.clientId, this.PosClientCredentials.clientSecret, cancellationToken);
            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            TokenResponse token = tokenResult.Data;
            Logger.LogDebug($"Token is {token.AccessToken}");
            this.CurrentUserToken = token;
            return Result.Success();
        }

        if (this.CurrentUserToken.Expires.UtcDateTime.Subtract(DateTime.UtcNow) < TimeSpan.FromMinutes(2))
        {
            Logger.LogDebug($"Token is about to expire at {this.CurrentUserToken.Expires.DateTime:O}");
            Result<TokenResponse> tokenResult = await this.SecurityServiceClient.GetToken(config.Username, config.Password, this.PosClientCredentials.clientId, this.PosClientCredentials.clientSecret, cancellationToken);
            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            TokenResponse token = tokenResult.Data;
            Logger.LogDebug($"Token is {token.AccessToken}");
            this.CurrentUserToken = token;
            return Result.Success();
        }

        return Result.Success();
    }

    private async Task StartupSequence(MerchantConfig cfg,
                                       CancellationToken cancellationToken) {
        // 1. Token
        Result tokenResult = await this.GetServiceToken(cancellationToken);
        if (tokenResult.IsFailed)
        {
            Logger.LogWarning($"Failed to get service token during startup sequence.");
            return;
        }

        tokenResult = await this.GetUserToken(cfg, cancellationToken);

        if (tokenResult.IsFailed) {
            Logger.LogWarning($"Failed to get token for merchant {cfg.MerchantName} during startup sequence.");
            return;
        }

        // 2. Load products
        List<Product> products = await ApiClient.GetProductList(cfg, this.CurrentUserToken, cancellationToken);
        cfg.Products = products;
        
        // 3. Balance
        decimal balance = await ApiClient.GetBalance(cfg, this.CurrentServiceToken, cancellationToken);
        await Repository.UpdateBalance(cfg.MerchantId,cfg.MerchantName, balance);
    }

    private async Task RunMainLoop(MerchantConfig cfg,
                                   CancellationToken token) {
        TimeSpan saleInterval = TimeSpan.FromSeconds(cfg.SaleIntervalSeconds);

        while (!token.IsCancellationRequested) {
            Merchant merchant = await this.Repository.GetMerchant(cfg.MerchantId);
            // Wait until the merchant's configured opening time
            DateTime currentTime = DateTime.Now;
            TimeSpan openingTime = cfg.OpeningTime.ToTimeSpan();
            TimeSpan closingTime = cfg.ClosingTime.ToTimeSpan();
            if (currentTime.TimeOfDay < openingTime) {
                TimeSpan delay = openingTime - currentTime.TimeOfDay;
                Logger.LogInformation($"Merchant {cfg.MerchantName} sleeping until opening time {cfg.OpeningTime}");
                await Task.Delay(delay, token);
            }

            if (currentTime.TimeOfDay > closingTime) {
                // Get last end of day time
                
                if (currentTime.Date > merchant.LastEndOfDayDateTime.Date) {
                    await DoReconciliation(cfg, token);
                }

                TimeSpan delay = openingTime - currentTime.TimeOfDay;
                if (delay < TimeSpan.Zero) {
                    delay += TimeSpan.FromDays(1);
                }

                Logger.LogInformation($"Merchant {cfg.MerchantName} sleeping until opening time {cfg.OpeningTime}");
                await Task.Delay(delay, token);
            }


            if (!cfg.Enabled) {
                Logger.LogInformation($"Merchant {cfg.MerchantName} is disabled. Sleeping.");
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                continue;
            }
            
            DateTime now = DateTime.Now;
            if (merchant.LastLogonDateTime.Date != now.Date)
            {
                Result tokenResult = await this.GetUserToken(cfg, token);
                if (tokenResult.IsFailed)
                {
                    Logger.LogWarning($"Failed to obtain token for daily logon for merchant {cfg.MerchantName}");
                }
                else
                {
                    await ApiClient.SendLogon(cfg, this.CurrentUserToken, merchant.TransactionNumber, token);
                    //_lastDailyLogonDate = now.Date;
                    await this.Repository.UpdateLastLogon(cfg.MerchantId, cfg.MerchantName, now);
                    Logger.LogInformation($"Performed daily logon for merchant {cfg.MerchantName} on {now:yyyy-MM-dd}");
                }
            }
            else {
                // Sell product
                await DoSaleCycle(cfg, merchant.TransactionNumber, token);
            }

            await this.Repository.IncrementTransactionNumber(cfg.MerchantId, cfg.MerchantName);
            await Task.Delay(saleInterval, token);
        }
    }

    private async Task DoSaleCycle(MerchantConfig cfg,Int32 transactionNumber, CancellationToken cancellationToken)
    {
        Result tokenResult = await this.GetServiceToken(cancellationToken);
        if (tokenResult.IsFailed)
            return;
        tokenResult = await this.GetUserToken(cfg, cancellationToken);
        if (tokenResult.IsFailed)
            return;

        decimal balance = await Repository.GetBalance(cfg.MerchantId);

        // Random product
        Product product = cfg.Products[_rng.Next(cfg.Products.Count)];

        Decimal value = product.Value switch
        {
            0 => this._rng.Next(9, 250),
            _ => product.Value
        };

        // Possible intentional fail
        bool induceFail = _rng.NextDouble() < cfg.FailureInjectionProbability;
        decimal saleValue = induceFail ? balance + 10 : value;
        
        SaleResponse result = await ApiClient.SendSale(cfg, this.CurrentUserToken, product, saleValue, transactionNumber, cancellationToken);

        if (result.Authorised)
        {
            Metrics.IncrementSales(cfg.MerchantId);
            await Repository.UpdateTotals(cfg.MerchantId, product.OperatorId, product.ContractId, saleValue);
            await Repository.UpdateBalance(cfg.MerchantId, cfg.MerchantName, balance - saleValue);
        }
        else {
            Metrics.IncrementFailedSales(cfg.MerchantId);
        }

        // Auto deposit?
        Decimal newBalance = await Repository.GetBalance(cfg.MerchantId);
        this.Metrics.SetBalance(cfg.MerchantId, newBalance);
        if (newBalance < cfg.DepositThreshold)
        {
            await ApiClient.SendDeposit(cfg, this.CurrentServiceToken, cfg.DepositAmount,cancellationToken);
            await Repository.UpdateBalance(cfg.MerchantId, cfg.MerchantName, newBalance + cfg.DepositAmount);
            newBalance = await Repository.GetBalance(cfg.MerchantId);
            this.Metrics.SetBalance(cfg.MerchantId, newBalance);
        }
    }

    private async Task DoReconciliation(MerchantConfig cfg, CancellationToken cancellationToken)
    {
        Result tokenResult = await this.GetUserToken(cfg, cancellationToken);
        if (tokenResult.IsFailed)
            return;

        List<OperatorTotal> totals = await Repository.GetTotals(cfg.MerchantId);
        await ApiClient.SendReconciliation(cfg, this.CurrentUserToken, totals, cancellationToken);

        // Clear totals
        await this.Repository.UpdateLastEndOfDay(cfg.MerchantId, cfg.MerchantName, DateTime.Now);
        await Repository.ClearTotals(cfg.MerchantId);

        this.Metrics.SetLastEndOfDay(cfg.MerchantId);
    }
}