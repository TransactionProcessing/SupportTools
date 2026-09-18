namespace HealthMonitoring.Tests;

public sealed class HostSmokeTests
{
    [Fact]
    public void Application_assembly_is_available()
    {
        Assert.NotNull(typeof(Program).Assembly);
    }
}
