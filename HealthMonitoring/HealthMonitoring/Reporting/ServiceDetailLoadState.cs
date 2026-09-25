namespace HealthMonitoring.Reporting;

public sealed record ServiceDetailLoadState(bool IsLoading, ServiceDetailModel? Model, string? ErrorMessage)
{
    public bool IsNotFound => !IsLoading && Model is null && ErrorMessage is null;

    public static ServiceDetailLoadState Loading { get; } = new(true, null, null);
    public static ServiceDetailLoadState NotFound { get; } = new(false, null, null);

    public static ServiceDetailLoadState Loaded(ServiceDetailModel model) => new(false, model, null);

    public static ServiceDetailLoadState Failed(string errorMessage) => new(false, null, errorMessage);
}
