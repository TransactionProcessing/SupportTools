# HealthMonitoring.Client

Reusable .NET client for service self-registration and dependency mapping.

For automatic registration at application startup, add this configuration:

```json
{
  "HealthMonitoring": {
    "MonitoringServerUrl": "http://localhost:9620/",
    "Service": {
      "ServiceId": "payments-api",
      "Name": "Payments API",
      "HealthUrl": "https://payments-api/health"
    },
    "Dependencies": [
      {
        "DependencyName": "Security Service",
        "TargetServiceId": "security-service"
      }
    ]
  }
}
```

Then add one line during service registration:

```csharp
services.AddHealthMonitoringRegistration(configuration);
```

The client registers the service and dependency mappings once during application startup. Registration is idempotent.
If the `HealthMonitoring` section is absent, no registration is attempted and the application starts normally.

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
