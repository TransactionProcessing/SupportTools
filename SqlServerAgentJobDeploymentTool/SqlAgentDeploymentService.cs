namespace SqlServerAgentJobDeploymentTool;

using Microsoft.Data.SqlClient;

internal sealed class SqlAgentDeploymentService
{
    private readonly SqlConnection _connection;
    private readonly TextWriter _writer;

    public SqlAgentDeploymentService(SqlConnection connection, TextWriter writer)
    {
        _connection = connection;
        _writer = writer;
    }

    public async Task DeployAsync(DeploymentManifest manifest, string? databaseNameOverride, CancellationToken cancellationToken)
    {
        foreach (JobDefinition job in manifest.Jobs)
        {
            await DeployJobAsync(job, databaseNameOverride, cancellationToken);
        }
    }

    private async Task DeployJobAsync(JobDefinition job, string? databaseNameOverride, CancellationToken cancellationToken)
    {
        if (await JobExistsAsync(job.Name, cancellationToken))
        {
            if (!job.ReplaceExisting)
            {
                throw new InvalidOperationException($"Job '{job.Name}' already exists and ReplaceExisting is false.");
            }

            await ExecuteProcedureAsync(
                "msdb.dbo.sp_delete_job",
                cancellationToken,
                new SqlParameter("@job_name", job.Name),
                new SqlParameter("@delete_unused_schedule", 1));
        }

        _writer.WriteLine($"Deploying job '{job.Name}'.");

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
            await ExecuteProcedureAsync(
                "msdb.dbo.sp_add_jobserver",
                cancellationToken,
                new SqlParameter("@job_id", jobId),
                new SqlParameter("@server_name", job.TargetServerName));
        }
        else
        {
            await ExecuteProcedureAsync(
                "msdb.dbo.sp_add_jobserver",
                cancellationToken,
                new SqlParameter("@job_id", jobId));
        }
    }

    private async Task AddJobStepAsync(Guid jobId, JobStepDefinition step, int stepId, string? databaseNameOverride, CancellationToken cancellationToken)
    {
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
    }

    private async Task AddJobScheduleAsync(Guid jobId, JobScheduleDefinition schedule, CancellationToken cancellationToken)
    {
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
        await using SqlCommand command = _connection.CreateCommand();
        command.CommandText = "select count_big(1) from msdb.dbo.sysjobs where name = @jobName;";
        command.Parameters.Add(new SqlParameter("@jobName", jobName));

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result) > 0;
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

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int ToDateInt(DateOnly date) => date.Year * 10000 + date.Month * 100 + date.Day;

    private static int ToTimeInt(TimeOnly time) => time.Hour * 10000 + time.Minute * 100 + time.Second;
}
