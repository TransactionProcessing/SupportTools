# Central logging Windows forwarder

This worker tails files matching `LogForwarder:FilePatterns` from configured directories, sends batches to `POST /api/logs`, and keeps a byte offset per file in a checkpoint JSON file. `IncludeSubdirectories` controls recursive scanning. The default pattern is `structured-logger-*.json*`, which also matches common numbered backup names such as `.1.json` and `.json.1`. Patterns are configurable for other naming schemes, such as `transaction*.json*`.

Each checkpoint records the file's identity and byte offset. The identity combines the file creation time with a hash of its first 1 KB, so it follows a file when rotation renames it and starts at offset zero when a rotated name is reused for a new file. After a complete scan, checkpoints for files no longer present are removed. A checkpoint is advanced only after a successful API response. If the process stops after the API accepts a batch but before the checkpoint is saved, the API's `eventId` deduplication makes replay safe. The worker maps NLog's `level` field to the API's `severity` field and preserves other NLog event properties.

Configure the shared log directories as sources. The logger writes each event to the shared file and may write a second copy under its tenant-specific directory; forwarding both would ingest duplicate logical events with distinct event IDs.

## Configure and run

Edit `appsettings.json` or provide environment variables:

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

The worker reads completed newline-terminated JSON records. A partially written last record waits until the next poll. Malformed JSON and incomplete or invalid structured records are forwarded as `Unknown` events with their exact source text in `rawLog`; valid records after them continue to be forwarded. Fallback event IDs are deterministic for a source file identity, byte position, and line content, so retrying after an API or checkpoint failure is deduplicated. Each batch log reports how many invalid lines it retained. File retention remains controlled by the logging service; the worker only follows the files still present during a successful scan.
