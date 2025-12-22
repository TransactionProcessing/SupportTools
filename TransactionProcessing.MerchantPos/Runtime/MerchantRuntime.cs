using MerchantPos.EF.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SecurityService.DataTransferObjects.Responses;
using SimpleResults;
using System.Threading;
using Microsoft.EntityFrameworkCore.Design;
using SecurityService.Client;
using Shared.Logger;
using Shared.Results;
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
    public async Task RunAsync((String clientId, String clientSecret) serviceClient, (String clientId, String clientSecret) posClient, MerchantConfig config, CancellationToken cancellationToken)
    {
        Logger.LogInformation($"MerchantRuntime started for {config.MerchantName}");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Get the service client token here ( can manage the expiry/caching at this level)
                Result<TokenResponse> serviceToken = await this.GetToken(this.CurrentServiceToken, serviceClient, cancellationToken);
                this.CurrentServiceToken = serviceToken.Data;

                await StartupSequence(posClient.clientId, posClient.clientSecret, config, cancellationToken);
                await RunMainLoop(posClient.clientId, posClient.clientSecret, config, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Runtime crashed for merchant {config.MerchantName}. Restarting in 5s...", ex);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task<Result<TokenResponse>> GetToken(TokenResponse currentToken, (String clientId, String clientSecret) serviceClient,
                                                       CancellationToken cancellationToken)
    {
        if (currentToken == null)
        {
            Result<TokenResponse> tokenResult = await this.SecurityServiceClient.GetToken(serviceClient.clientId, serviceClient.clientSecret, cancellationToken);
            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            TokenResponse token = tokenResult.Data;
            Logger.LogDebug($"Token is {token.AccessToken}");
            return Result.Success(token);
        }

        if (currentToken.Expires.UtcDateTime.Subtract(DateTime.UtcNow) < TimeSpan.FromMinutes(2))
        {
            Logger.LogDebug($"Token is about to expire at {currentToken.Expires.DateTime:O}");
            Result<TokenResponse> tokenResult = await this.SecurityServiceClient.GetToken(serviceClient.clientId, serviceClient.clientSecret, cancellationToken);
            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            TokenResponse token = tokenResult.Data;
            Logger.LogDebug($"Token is {token.AccessToken}");
            return Result.Success(token);
        }

        return Result.Success(currentToken);
    }

    private async Task StartupSequence(String clientId,
                                       String clientSecret,
                                       MerchantConfig cfg,
                                       CancellationToken cancellationToken) {
        // 1. Token
        Result<TokenResponse> tokenResult = await this.ApiClient.GetToken(clientId, clientSecret, cfg, cancellationToken);

        if (tokenResult.IsFailed) {
            Logger.LogWarning($"Failed to get token for merchant {cfg.MerchantName} during startup sequence.");
            return;
        }

        // 2. Load products
        var products = await ApiClient.GetProductList(cfg, tokenResult.Data, cancellationToken);
        cfg.Products = products;
        
        // 3. Balance
        decimal balance = await ApiClient.GetBalance(cfg, this.CurrentServiceToken, cancellationToken);
        await Repository.UpdateBalance(cfg.MerchantId,cfg.MerchantName, balance);
    }

    private async Task RunMainLoop(String clientId,
                                   String clientSecret,
                                   MerchantConfig cfg,
                                   CancellationToken token) {
        TimeSpan saleInterval = TimeSpan.FromSeconds(cfg.SaleIntervalSeconds);

        while (!token.IsCancellationRequested) {
            var merchant = await this.Repository.GetMerchant(cfg.MerchantId);
            // Wait until the merchant's configured opening time
            var currentTime = DateTime.Now;
            var openingTime = cfg.OpeningTime.ToTimeSpan();
            var closingTime = cfg.ClosingTime.ToTimeSpan();
            if (currentTime.TimeOfDay < openingTime) {
                TimeSpan delay = openingTime - currentTime.TimeOfDay;
                Logger.LogInformation($"Merchant {cfg.MerchantName} sleeping until opening time {cfg.OpeningTime}");
                await Task.Delay(delay, token);
            }

            if (currentTime.TimeOfDay > closingTime) {
                // Get last end of day time
                
                if (currentTime.Date > merchant.LastEndOfDayDateTime.Date) {
                    await DoReconciliation(clientId, clientSecret, cfg, token);
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
            
            var now = DateTime.Now;
            if (merchant.LastLogonDateTime.Date != now.Date)
            {
                var tokenResult = await this.ApiClient.GetToken(clientId, clientSecret, cfg, token);
                if (tokenResult.IsFailed)
                {
                    Logger.LogWarning($"Failed to obtain token for daily logon for merchant {cfg.MerchantName}");
                }
                else
                {
                    await ApiClient.SendLogon(cfg, tokenResult.Data, merchant.TransactionNumber, token);
                    //_lastDailyLogonDate = now.Date;
                    await this.Repository.UpdateLastLogon(cfg.MerchantId, cfg.MerchantName, now);
                    Logger.LogInformation($"Performed daily logon for merchant {cfg.MerchantName} on {now:yyyy-MM-dd}");
                }
            }
            else {
                // Sell product
                await DoSaleCycle(clientId, clientSecret, cfg, merchant.TransactionNumber, token);
            }

            await this.Repository.IncrementTransactionNumber(cfg.MerchantId, cfg.MerchantName);
            await Task.Delay(saleInterval, token);
        }
    }

    private async Task DoSaleCycle(String clientId, String clientSecret, MerchantConfig cfg,Int32 transactionNumber, CancellationToken cancellationToken)
    {
        Result<TokenResponse> tokenResult = await this.ApiClient.GetToken(clientId, clientSecret, cfg, cancellationToken);
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
        
        SaleResponse result = await ApiClient.SendSale(cfg, tokenResult.Data, product, saleValue, transactionNumber, cancellationToken);

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

    private async Task DoReconciliation(String clientId, String clientSecret, MerchantConfig cfg, CancellationToken cancellationToken)
    {
        var tokenResult = await this.ApiClient.GetToken(clientId, clientSecret, cfg, cancellationToken);
        if (tokenResult.IsFailed)
            return;

        var totals = await Repository.GetTotals(cfg.MerchantId);
        await ApiClient.SendReconciliation(cfg, tokenResult.Data, totals, cancellationToken);

        // Clear totals
        await this.Repository.UpdateLastEndOfDay(cfg.MerchantId, cfg.MerchantName, DateTime.Now);
        await Repository.ClearTotals(cfg.MerchantId);

        this.Metrics.SetLastEndOfDay(cfg.MerchantId);
    }
}