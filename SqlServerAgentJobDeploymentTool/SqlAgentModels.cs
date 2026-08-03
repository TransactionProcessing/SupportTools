namespace SqlServerAgentJobDeploymentTool;

using System.Text.Json.Serialization;

public sealed class DeploymentManifest
{
    public string? ConnectionString { get; set; }

    public List<JobDefinition> Jobs { get; set; } = [];
}

public sealed class JobDefinition
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    public bool ReplaceExisting { get; set; } = true;

    public string? OwnerLoginName { get; set; }

    public string? CategoryName { get; set; }

    public string? TargetServerName { get; set; }

    public int? DeleteLevel { get; set; }

    public List<JobStepDefinition> Steps { get; set; } = [];

    public List<JobScheduleDefinition> Schedules { get; set; } = [];
}

public sealed class JobStepDefinition
{
    public int? StepId { get; set; }

    public string Name { get; set; } = string.Empty;

    public JobSubsystem Subsystem { get; set; } = JobSubsystem.TSql;

    public string Command { get; set; } = string.Empty;

    public JobStepAction OnSuccessAction { get; set; } = JobStepAction.QuitWithSuccess;

    public int? OnSuccessStepId { get; set; }

    public JobStepAction OnFailAction { get; set; } = JobStepAction.QuitWithFailure;

    public int? OnFailStepId { get; set; }

    public int? RetryAttempts { get; set; }

    public int? RetryIntervalMinutes { get; set; }

    public string? ProxyName { get; set; }

    public string? OutputFileName { get; set; }

    public bool AppendToOutputFile { get; set; }
}

public sealed class JobScheduleDefinition
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public JobScheduleFrequency FrequencyType { get; set; } = JobScheduleFrequency.Daily;

    public int FrequencyInterval { get; set; } = 1;

    public int FrequencyRelativeInterval { get; set; } = 1;

    public int FrequencyRecurrenceFactor { get; set; } = 1;

    public JobScheduleSubdayType FrequencySubdayType { get; set; } = JobScheduleSubdayType.Once;

    public int FrequencySubdayInterval { get; set; } = 0;

    public DateOnly? ActiveStartDate { get; set; }

    public TimeOnly? ActiveStartTime { get; set; }

    public DateOnly? ActiveEndDate { get; set; }

    public TimeOnly? ActiveEndTime { get; set; }
}

public enum JobSubsystem
{
    TSql = 1,
    CmdExec = 2,
    PowerShell = 3,
    AnalysisServicesCommand = 4,
    SSIS = 5
}

public enum JobStepAction
{
    QuitWithSuccess = 1,
    QuitWithFailure = 2,
    GoToNextStep = 3,
    GoToStep = 4
}

public enum JobScheduleFrequency
{
    OneTime = 1,
    Daily = 4,
    Weekly = 8,
    Monthly = 16,
    MonthlyRelative = 32,
    AgentStart = 64,
    Idle = 128
}

public enum JobScheduleSubdayType
{
    Once = 1,
    Seconds = 2,
    Minutes = 4,
    Hours = 8
}
