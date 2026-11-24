# SQL Server Agent Job Deployment Tool

This utility deploys SQL Server Agent jobs from a JSON manifest.

## Usage

```text
SqlServerAgentJobDeploymentTool --manifest sql-agent-jobs.sample.json
```

For a deployment-local configuration, keep `sql-agent-jobs.local.json` beside the executable and run:

```text
SqlServerAgentJobDeploymentTool --manifest sql-agent-jobs.local.json
```

The local manifest must include `connectionString`. Keep this file on the deployment server; it is excluded from source control. The GitHub deployment workflows expect it at `C:\txnproc\transactionprocessing\sqlagentjobdeploymenttool\sql-agent-jobs.local.json` on each environment's runner.

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
- Each T-SQL step can set its own `databaseName`, allowing one job to run steps against multiple databases on the connected SQL Server.
- If a step has no `databaseName`, the target database can be supplied at deploy time from the CLI or UI. This override remains a fallback for steps without an explicit database.
- Set `onFailAction` to `goToNextStep` on steps that should allow the job to continue after an error. The step history records failures; the last step's outcome determines whether the job quits with success or failure.

For multiple estates on the same SQL Server, add one step per estate and set each step's `databaseName` to that estate's database. Set `onFailAction` to `goToNextStep` on each estate step so a failed estate does not prevent later estates from running.

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
