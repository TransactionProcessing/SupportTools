# Central logging Windows forwarder

This worker tails files matching `LogForwarder:FilePatterns` from configured directories, sends batches to `POST /api/logs`, and keeps a byte offset per file in a checkpoint JSON file. `IncludeSubdirectories` controls recursive scanning. The default pattern is `structured-logger-*.json*`, which also matches common numbered backup names such as `.1.json` and `.json.1`. Patterns are configurable for other naming schemes, such as `transaction*.json*`.

Each checkpoint records the file's identity, byte offset, and a digest of up to 1 KB immediately before that offset. On Windows, identity uses the volume serial number and file index of the open handle; on Linux, it uses device and inode. Both identities also include a hash of the first 1 KB, so files with recycled IDs and different prefixes restart from byte zero while append-only writes preserve identity. The saved boundary digest detects rewrites or truncation followed by regrowth when the bytes immediately before the saved offset have changed. Reusing a path for a different file starts at byte zero. A copy or restored file has a new identity and replays from byte zero; the API's `eventId` deduplication makes replay safe. Other platforms use the path, creation time, and first-1-KB hash as a stable append-safe fallback, favoring replay after rename over reusing an offset for a different file. Checkpoint updates are written to a temporary file and atomically replace the prior checkpoint. After a complete scan, checkpoints for files no longer present are removed. A checkpoint is advanced only after a successful API response. The worker maps NLog's `level` field to the API's `severity` field and preserves other NLog event properties.

Configure the shared log directories as sources. The logger writes each event to the shared file and may write a second copy under its tenant-specific directory; forwarding both would ingest duplicate logical events with distinct event IDs.

## Configure and run

The worker loads optional `hosting.json` and `hosting.{Environment}.json` files beside its executable, then `appsettings.json` and `appsettings.{Environment}.json` from the shared config folder, then the same appsettings files beside the executable, and finally `appsettings.local.json` from the shared config folder. The shared config folder is `C:\home\txnproc\config` on Windows and `/home/txnproc/config` elsewhere. Later files override earlier files; environment variables are applied last. The deployment writes the forwarder settings to the shared `appsettings.local.json`. Add a repository-local `appsettings.local.json` to `.gitignore` if you use one for development.

```powershell
$env:LogForwarder__SourceDirectories__0 = 'C:\services\payment\logs\shared'
$env:LogForwarder__SourceDirectories__1 = 'D:\services\orders\logs\shared'
$env:LogForwarder__FilePatterns__0 = 'transaction*.json*'
$env:LogForwarder__Endpoint = 'https://central-logs.example/api/logs'
$env:LogForwarder__ApiKey = 'your-ingestion-key'
$env:LogForwarder__CheckpointPath = 'C:\ProgramData\CentralLogging\checkpoint.json'
dotnet run --project CentralLogging.Forwarder
```

When hosted by Windows Service, set the same environment variables for the service account and install the published executable with `sc.exe`. Give the service account read access to source log directories and write access to the checkpoint directory. Use HTTPS for the API endpoint.

The GitHub deployment workflows install this worker as the `Central Logging Forwarder` Windows service. The deployment reads `LogForwarder:SourceDirectories` from the shared `appsettings.local.json` first; `CENTRAL_LOGGING_SOURCE_DIRECTORIES` is used only when no directories are configured there. If using the environment variable, provide a JSON array such as `["C:\\services\\payment\\logs\\shared"]`. The deployment reads `LogForwarder:Endpoint` from the shared `appsettings.local.json` first; `CENTRAL_LOGGING_ENDPOINT` is used only when no endpoint is configured there. The endpoint should be the API's ingestion URL, such as `http://127.0.0.1:7100/api/logs` when both services are on the same server. `CENTRAL_LOGGING_API_KEY` is an optional secret override; if omitted, configure a non-default `LogForwarder:ApiKey` or `LogIngestion:ApiKey` in the shared `appsettings.local.json`. Deployment stops with an error if neither key source provides a non-default key. Use the same key for the API and forwarder. Deployment writes these settings to `C:\home\txnproc\config\appsettings.local.json` and stores checkpoints under `C:\ProgramData\CentralLogging`.

The worker reads completed newline-terminated JSON records. A partially written last record waits until the next poll. Malformed JSON and incomplete or invalid structured records are forwarded as `Unknown` events with their exact source text in `rawLog`; valid records after them continue to be forwarded. Fallback event IDs are deterministic for a source file identity, byte position, and line content, so retrying after an API or checkpoint failure is deduplicated. Each batch log reports how many invalid lines it retained. File retention remains controlled by the logging service; the worker only follows the files still present during a successful scan.

The forwarder writes plain-text diagnostic logs to daily files under `C:\home\txnproc\trace` on Windows or `/home/txnproc/trace` elsewhere. Set `CENTRAL_LOGGING_TRACE_DIRECTORY` to override the folder. Files are named `central-logging-forwarder-yyyyMMdd.log` and are not automatically deleted.
