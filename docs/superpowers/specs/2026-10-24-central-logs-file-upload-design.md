# Central Logs File Upload Design

## Intent

Add an option to the Central Logs UI to upload an existing structured log file into the database. Users can backfill logs through the same API key protected by the UI.

## Goals

- Accept the newline-delimited JSON event format already read by the Central Logging forwarder.
- Reuse existing event validation, duplicate detection, persistence, and structured-field catalog updates.
- Show counts for imported, duplicate, and invalid lines after the upload.

## Non-goals

- Parse plain text logs or introduce new log formats.
- Change the forwarder or the existing JSON batch ingestion contract.

## Constraints and behavior

- The UI keeps the configured API key in memory and sends it in `X-Log-Api-Key`.
- Uploaded files contain one JSON event per line. Malformed or invalid lines are skipped and counted; valid lines are ingested in batches no larger than the existing 500-event limit.
- Existing `eventId` deduplication remains the authority for identifying already stored events.
- The upload result reports accepted, duplicate, and invalid line totals. Failures to persist a batch are returned as upload errors.
- Search results represent the database query, not a preview limited to the most recently uploaded file. A successful upload clears the old results and prompts the user to search again.

## Approaches

1. **API accepts and processes the file (chosen).** Add an authenticated upload endpoint that streams/reads newline-delimited events, validates and ingests bounded batches using existing logic, and returns a summary. The UI adds a file selector, upload action, progress/status messaging, and the final counts. This keeps format handling with the ingestion service and avoids loading the whole file into browser memory.
2. **Browser parses and posts batches to the existing endpoint.** This avoids an API route change, but moves ingestion parsing and batch state into the UI and makes browser memory and retry behavior more significant for large files.

The server-side endpoint is chosen because it places file parsing next to the existing validation and persistence logic, while keeping the UI focused on file selection and feedback.

## Builder decisions

- Use a multipart upload route under `/api/logs` so it receives the same API-key middleware protection.
- Process newline-delimited JSON in bounded batches and preserve the existing idempotent `eventId` behavior.
- Add the upload control to the existing Central Logs page and keep it disabled until connected.
- Update the API README with the supported file format and result behavior.
