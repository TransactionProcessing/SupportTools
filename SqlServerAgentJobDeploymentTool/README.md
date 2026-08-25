# SQL Server Agent Job Deployment Tool

This utility deploys SQL Server Agent jobs from a JSON manifest.

## Usage

```text
SqlServerAgentJobDeploymentTool --manifest sql-agent-jobs.sample.json
```

Optional overrides:

- `--connection-string <value>`
- `--database-name <value>`
- `--dry-run`
- `--help`

Logging is written through NLog to the console and to `%LOCALAPPDATA%\SqlServerAgentJobDeploymentTool\Logs\sql-agent-job-deployment.log`.

## Manifest shape

- `connectionString`: SQL Server connection string for `msdb`.
- `jobs[]`: one or more job definitions.
- Each job contains `steps[]` and optional `schedules[]`.
- The target database is supplied at deploy time from the CLI or the UI. When set, it overrides the database for all T-SQL steps.

The sample manifest includes the existing settlement and scavenge jobs from this repository.

## Read model job

For the `TransactionProcessorReadModel-435613ac-a468-47a3-ac4f-649d89764c22` database, use:

```text
SqlServerAgentJobDeploymentTool --manifest transactionprocessor-readmodel-transaction-jobs.json
```

That manifest deploys one job with two separate steps:

1. `spBuildTodaysTransactions`
2. `spBuildHistoricTransactions`

It is scheduled to run every 5 minutes.

You can set the target database in the UI using the `Database name override` field, or on the command line with `--database-name`.
