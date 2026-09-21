namespace HealthMonitoring.Components;

public static class DashboardApiUri
{
    public static Uri Create(string baseUri, string relativePath) => new(new Uri(baseUri, UriKind.Absolute), relativePath);
}
