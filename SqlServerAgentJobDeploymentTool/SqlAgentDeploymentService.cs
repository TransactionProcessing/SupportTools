namespace SqlServerAgentJobDeploymentTool;

using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

internal sealed class SqlAgentDeploymentService
{
    private readonly SqlConnection _connection;
    private readonly TextWriter _writer;
    private readonly ILogger<SqlAgentDeploymentService> _logger;

    public SqlAgentDeploymentService(SqlConnection connection, TextWriter writer, ILogger<SqlAgentDeploymentService> logger)
    {
        _connection = connection;
        _writer = writer;
        _logger = logger;
    }

    public async Task DeployAsync(DeploymentManifest manifest, string? databaseNameOverride, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting deployment for {JobCount} job(s). Database override: {DatabaseOverride}.",
            manifest.Jobs.Count,
            string.IsNullOrWhiteSpace(databaseNameOverride) ? "<none>" : databaseNameOverride);

        foreach (JobDefinition job in manifest.Jobs)
        {
            await DeployJobAsync(job, databaseNameOverride, cancellationToken);
        }

        _logger.LogInformation("Completed deployment for {JobCount} job(s).", manifest.Jobs.Count);
    }

    private async Task DeployJobAsync(JobDefinition job, string? databaseNameOverride, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Evaluating job {JobName}.", job.Name);

        if (await JobExistsAsync(job.Name, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} already exists.", job.Name);
            if (!job.ReplaceExisting)
            {
                _logger.LogWarning("Job {JobName} exists and ReplaceExisting is false.", job.Name);
                throw new InvalidOperationException($"Job '{job.Name}' already exists and ReplaceExisting is false.");
            }

            _logger.LogInformation("Replacing existing job {JobName}.", job.Name);
            await ExecuteProcedureAsync(
                "msdb.dbo.sp_delete_job",
                cancellationToken,
                new SqlParameter("@job_name", job.Name),
                new SqlParameter("@delete_unused_schedule", 1));
        }

        _writer.WriteLine($"Deploying job '{job.Name}'.");
        _logger.LogInformation("Deploying job {JobName} with {StepCount} step(s) and {ScheduleCount} schedule(s).",
            job.Name,
            job.Steps.Count,
            job.Schedules.Count);

        int deleteLevel = job.DeleteLevel ?? 0;
        SqlParameter jobIdParameter = new("@job_id", System.Data.SqlDbType.UniqueIdentifier)
        {
            Direction = System.Data.ParameterDirection.Output
        };

        List<SqlParameter> jobParameters =
        [
            new SqlParameter("@job_name", job.Name),
            new SqlParameter("@enabled", job.Enabled),
            new SqlParameter("@description", string.IsNullOrWhiteSpace(job.Description) ? DBNull.Value : job.Description),
            new SqlParameter("@owner_login_name", string.IsNullOrWhiteSpace(job.OwnerLoginName) ? DBNull.Value : job.OwnerLoginName),
            new SqlParameter("@category_name", string.IsNullOrWhiteSpace(job.CategoryName) ? DBNull.Value : job.CategoryName),
            new SqlParameter("@delete_level", deleteLevel),
            jobIdParameter
        ];

        await ExecuteProcedureAsync("msdb.dbo.sp_add_job", jobParameters, cancellationToken);

        Guid jobId = (Guid)jobIdParameter.Value;
        _logger.LogInformation("Created job {JobName} with job id {JobId}.", job.Name, jobId);

        int nextStepId = 1;
        foreach (JobStepDefinition step in job.Steps)
        {
            int stepId = step.StepId ?? nextStepId;
            nextStepId = stepId + 1;

            await AddJobStepAsync(jobId, step, stepId, databaseNameOverride, cancellationToken);
        }

        foreach (JobScheduleDefinition schedule in job.Schedules)
        {
            await AddJobScheduleAsync(jobId, schedule, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(job.TargetServerName))
        {
            _logger.LogInformation("Binding job {JobName} to target server {TargetServer}.", job.Name, job.TargetServerName);
            await ExecuteProcedureAsync(
                "msdb.dbo.sp_add_jobserver",
                cancellationToken,
                new SqlParameter("@job_id", jobId),
                new SqlParameter("@server_name", job.TargetServerName));
        }
        else
        {
            _logger.LogInformation("Binding job {JobName} to the local server.", job.Name);
            await ExecuteProcedureAsync(
                "msdb.dbo.sp_add_jobserver",
                cancellationToken,
                new SqlParameter("@job_id", jobId));
        }

        _logger.LogInformation("Finished job {JobName}.", job.Name);
    }

    private async Task AddJobStepAsync(Guid jobId, JobStepDefinition step, int stepId, string? databaseNameOverride, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Adding step {StepId} for job {JobId}: {StepName}.", stepId, jobId, step.Name);

        List<SqlParameter> parameters =
        [
            new SqlParameter("@job_id", jobId),
            new SqlParameter("@step_id", stepId),
            new SqlParameter("@step_name", step.Name),
            new SqlParameter("@subsystem", ToSubsystemName(step.Subsystem)),
            new SqlParameter("@command", step.Command),
            new SqlParameter("@database_name", string.IsNullOrWhiteSpace(databaseNameOverride) ? DBNull.Value : databaseNameOverride),
            new SqlParameter("@on_success_action", (int)step.OnSuccessAction),
            new SqlParameter("@on_success_step_id", step.OnSuccessStepId ?? 0),
            new SqlParameter("@on_fail_action", (int)step.OnFailAction),
            new SqlParameter("@on_fail_step_id", step.OnFailStepId ?? 0),
            new SqlParameter("@retry_attempts", step.RetryAttempts ?? 0),
            new SqlParameter("@retry_interval", step.RetryIntervalMinutes ?? 0),
            new SqlParameter("@output_file_name", string.IsNullOrWhiteSpace(step.OutputFileName) ? DBNull.Value : step.OutputFileName),
            new SqlParameter("@flags", step.AppendToOutputFile ? 2 : 0)
        ];

        if (!string.IsNullOrWhiteSpace(step.ProxyName))
        {
            parameters.Add(new SqlParameter("@proxy_name", step.ProxyName));
        }

        await ExecuteProcedureAsync("msdb.dbo.sp_add_jobstep", parameters, cancellationToken);

        _writer.WriteLine($"  Added step {stepId}: {step.Name}");
        _logger.LogInformation("Added step {StepId} for job {JobId}: {StepName}.", stepId, jobId, step.Name);
    }

    private async Task AddJobScheduleAsync(Guid jobId, JobScheduleDefinition schedule, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Adding schedule {ScheduleName} for job {JobId}.", schedule.Name, jobId);

        List<SqlParameter> parameters =
        [
            new SqlParameter("@job_id", jobId),
            new SqlParameter("@name", schedule.Name),
            new SqlParameter("@enabled", schedule.Enabled),
            new SqlParameter("@freq_type", (int)schedule.FrequencyType),
            new SqlParameter("@freq_interval", schedule.FrequencyInterval),
            new SqlParameter("@freq_subday_type", (int)schedule.FrequencySubdayType),
            new SqlParameter("@freq_subday_interval", schedule.FrequencySubdayInterval),
            new SqlParameter("@freq_relative_interval", schedule.FrequencyRelativeInterval),
            new SqlParameter("@freq_recurrence_factor", schedule.FrequencyRecurrenceFactor),
            new SqlParameter("@active_start_date", ToDateInt(schedule.ActiveStartDate ?? DateOnly.FromDateTime(DateTime.Today))),
            new SqlParameter("@active_end_date", schedule.ActiveEndDate is null ? 99991231 : ToDateInt(schedule.ActiveEndDate.Value)),
            new SqlParameter("@active_start_time", ToTimeInt(schedule.ActiveStartTime ?? TimeOnly.MinValue)),
            new SqlParameter("@active_end_time", schedule.ActiveEndTime is null ? 235959 : ToTimeInt(schedule.ActiveEndTime.Value))
        ];

        await ExecuteProcedureAsync("msdb.dbo.sp_add_jobschedule", parameters, cancellationToken);

        _writer.WriteLine($"  Added schedule: {schedule.Name}");
        _logger.LogInformation("Added schedule {ScheduleName} for job {JobId}.", schedule.Name, jobId);
    }

    private static string ToSubsystemName(JobSubsystem subsystem) => subsystem switch
    {
        JobSubsystem.TSql => "TSQL",
        JobSubsystem.CmdExec => "CmdExec",
        JobSubsystem.PowerShell => "PowerShell",
        JobSubsystem.AnalysisServicesCommand => "ANALYSISCOMMAND",
        JobSubsystem.SSIS => "SSIS",
        _ => throw new ArgumentOutOfRangeException(nameof(subsystem), subsystem, "Unsupported SQL Agent subsystem.")
    };

    private async Task<bool> JobExistsAsync(string jobName, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Checking whether job {JobName} exists.", jobName);

        await using SqlCommand command = _connection.CreateCommand();
        command.CommandText = "select count_big(1) from msdb.dbo.sysjobs where name = @jobName;";
        command.Parameters.Add(new SqlParameter("@jobName", jobName));

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        bool exists = Convert.ToInt64(result) > 0;
        _logger.LogTrace("Job {JobName} exists: {Exists}.", jobName, exists);
        return exists;
    }

    private async Task ExecuteProcedureAsync(string procedureName, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await ExecuteProcedureAsync(procedureName, (IEnumerable<SqlParameter>)parameters, cancellationToken);
    }

    private async Task ExecuteProcedureAsync(string procedureName, IEnumerable<SqlParameter> parameters, CancellationToken cancellationToken)
    {
        await using SqlCommand command = _connection.CreateCommand();
        command.CommandType = System.Data.CommandType.StoredProcedure;
        command.CommandText = procedureName;

        foreach (SqlParameter parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.LogTrace("Executing stored procedure {ProcedureName}.", procedureName);
        await command.ExecuteNonQueryAsync(cancellationToken);
        stopwatch.Stop();
        _logger.LogTrace("Executed stored procedure {ProcedureName} in {ElapsedMilliseconds} ms.", procedureName, stopwatch.ElapsedMilliseconds);
    }

    private static int ToDateInt(DateOnly date) => date.Year * 10000 + date.Month * 100 + date.Day;

    private static int ToTimeInt(TimeOnly time) => time.Hour * 10000 + time.Minute * 100 + time.Second;
}
