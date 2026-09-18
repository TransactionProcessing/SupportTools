# HealthMonitoring.Client

Reusable .NET client for service self-registration and dependency mapping.

```csharp
services.AddHealthMonitoringRegistrationClient(options =>
{
    options.MonitoringServerUrl = new Uri("http://localhost:9620");
});

var result = await client.RegisterAndConfigureAsync(
    new ServiceRegistrationOptions
    {
        ServiceId = "payments-api",
        Name = "Payments API",
        HealthUrl = new Uri("https://payments-api/health")
    },
    [new DependencyMappingOptions("Security Service", "security-service")]);
```

Registration is idempotent. Dependency mappings are resolved by `TargetServiceId` and are created or updated as needed.
