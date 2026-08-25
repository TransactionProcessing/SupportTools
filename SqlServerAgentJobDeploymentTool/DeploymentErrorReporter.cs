namespace SqlServerAgentJobDeploymentTool;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;

internal static class DeploymentErrorReporter
{
    public static string DescribeConnectionTarget(string connectionString)
    {
        try
        {
            SqlConnectionStringBuilder builder = new(connectionString);
            string authMode = builder.IntegratedSecurity ? "Windows auth" : "SQL auth";
            string server = string.IsNullOrWhiteSpace(builder.DataSource) ? "<unknown server>" : builder.DataSource;
            string database = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "<unknown database>" : builder.InitialCatalog;
            return $"{server}/{database} ({authMode})";
        }
        catch
        {
            return "<unparseable connection string>";
        }
    }

    public static string GetUserMessage(Exception exception, string operation, string? context = null)
    {
        string contextSuffix = string.IsNullOrWhiteSpace(context) ? string.Empty : $" {context}";

        return exception switch
        {
            OperationCanceledException => $"Operation canceled while {operation}.",
            FileNotFoundException fileNotFound => $"Unable to {operation}.{contextSuffix} {fileNotFound.Message}".Trim(),
            UnauthorizedAccessException => $"Access was denied while {operation}.{contextSuffix}".Trim(),
            SqlException sqlException => BuildSqlMessage(sqlException, operation, context),
            JsonException => $"The manifest content is not valid JSON while {operation}.{contextSuffix}".Trim(),
            InvalidOperationException invalidOperation => invalidOperation.Message,
            IOException ioException => $"An I/O error occurred while {operation}.{contextSuffix} {ioException.Message}".Trim(),
            _ => $"Unexpected error while {operation}.{contextSuffix}".Trim()
        };
    }

    public static void ReportCli(Exception exception, ILogger logger, TextWriter errorWriter, string operation, string? context = null)
    {
        logger.LogError(exception, "{Operation} failed. {Context}", operation, context ?? string.Empty);
        errorWriter.WriteLine(GetUserMessage(exception, operation, context));
    }

    public static void ReportUi(Exception exception, ILogger logger, Action<string> appendOutput, string operation, string? context = null)
    {
        logger.LogError(exception, "{Operation} failed. {Context}", operation, context ?? string.Empty);
        appendOutput(GetUserMessage(exception, operation, context));
    }

    private static string BuildSqlMessage(SqlException exception, string operation, string? context)
    {
        string contextSuffix = string.IsNullOrWhiteSpace(context) ? string.Empty : $" for {context}";

        return exception.Number switch
        {
            18456 => $"SQL authentication failed{contextSuffix} while {operation}.",
            53 => $"SQL Server was not found or was not accessible{contextSuffix} while {operation}.",
            2 => $"A network-related or instance-specific error occurred{contextSuffix} while {operation}.",
            _ => $"SQL Server reported an error{contextSuffix} while {operation}: {exception.Message}"
        };
    }
}
