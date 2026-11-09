# SupportTools

## Central Logging tests

Run the Central Logging API and forwarder tests with:

```powershell
dotnet test CentralLogging.Tests/CentralLogging.Tests.csproj --configuration Release
```

SQL Server integration tests start a disposable SQL Server container automatically and require Docker. On Windows with WSL2, the fixture discovers the WSL host IP for the container's mapped port, so the command stays the same. Set `CENTRAL_LOGGING_TEST_CONNECTION` to use an existing SQL Server instead; the tests apply the Central Logging migrations and need permission to create or update the configured test database. For other remote Docker hosts, set `TESTCONTAINERS_HOST_OVERRIDE` to the host address exposing mapped ports. The pull request workflow uses the same test-managed container setup.

## Health Monitoring

The standalone `HealthMonitoring` application is an ASP.NET Core/Blazor Server monitor for ASP.NET health endpoints. It uses SQL Server for service configuration, health observations, incidents, and history.

Run locally with:

```powershell
dotnet run --project HealthMonitoring/HealthMonitoring.csproj
```

Set `ConnectionStrings:HealthMonitoring` (or `HealthMonitoring__ConnectionStrings__HealthMonitoring`) to a SQL Server connection string before enabling persistence and migrations. The application applies pending EF Core migrations on startup.

The checked-in local sample uses SQL authentication with user `sa` and the dummy password `ChangeThisStrongPassword!123`. Replace it with the actual local `sa` password, preferably through an environment variable or user secrets.

A local SQL Server plus the monitor can be started with:

```powershell
docker compose -f HealthMonitoring/docker-compose.sqlserver.yml up --build
```

Register a service with:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/services/register -ContentType 'application/json' -Body (@{
  serviceId = 'merchant-pos-production'
  name = 'Merchant POS'
  environment = 'Production'
  healthUrl = 'https://merchant-pos/health'
  pollingIntervalSeconds = 60
  requestTimeoutSeconds = 10
  retentionDays = 365
} | ConvertTo-Json)
```
