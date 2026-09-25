using HealthMonitoring.Reporting;

namespace HealthMonitoring.Tests.Reporting;

public sealed class ServiceDetailLoadStateTests
{
    [Fact]
    public void Loading_state_is_not_reported_as_not_found()
    {
        var state = ServiceDetailLoadState.Loading;

        Assert.True(state.IsLoading);
        Assert.False(state.IsNotFound);
        Assert.Null(state.ErrorMessage);
    }

    [Fact]
    public void Missing_state_is_reported_as_not_found()
    {
        var state = ServiceDetailLoadState.NotFound;

        Assert.False(state.IsLoading);
        Assert.True(state.IsNotFound);
        Assert.Null(state.ErrorMessage);
    }

    [Fact]
    public void Failed_state_exposes_error_without_being_reported_as_not_found()
    {
        var state = ServiceDetailLoadState.Failed("Database query failed.");

        Assert.False(state.IsLoading);
        Assert.False(state.IsNotFound);
        Assert.Equal("Database query failed.", state.ErrorMessage);
    }
}
