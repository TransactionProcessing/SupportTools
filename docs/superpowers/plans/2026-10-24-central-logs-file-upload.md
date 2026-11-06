# Central Logs File Upload Implementation Plan

> **For agentic workers:** Use the native execution approach to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a connected Central Logs UI user upload a newline-delimited JSON log file and import its valid events into SQL Server.

**Architecture:** Add a multipart `/api/logs/upload` endpoint that parses lines and feeds bounded batches into the same validation, deduplication, persistence, and structured-field catalog flow as `POST /api/logs`. Add a connected-only upload panel to the existing static UI and show accepted, duplicate, and invalid-line totals.

**Tech Stack:** ASP.NET Core minimal APIs on .NET 10, EF Core, SQL Server, static HTML/CSS/JavaScript.

**Spec:** `docs/superpowers/specs/2026-10-24-central-logs-file-upload-design.md`

## Global Constraints

- Accept the newline-delimited JSON event format already read by the Central Logging forwarder.
- Reuse existing event validation, duplicate detection, persistence, and structured-field catalog updates.
- Uploaded files contain one JSON event per line. Malformed or invalid lines are skipped and counted; valid lines are ingested in batches no larger than the existing 500-event limit.
- Existing `eventId` deduplication remains the authority for identifying already stored events.
- The upload result reports accepted, duplicate, and invalid line totals. Failures to persist a batch are returned as upload errors.

## Review Focus

- Empty file or blank lines: return zero accepted and count no blank lines as invalid.
- Malformed JSON and missing required fields: skip and count each nonblank bad line.
- Duplicate IDs, including repeated IDs in the uploaded file: count as duplicates using existing ingestion behavior.
- A failure during persistence: return an error instead of reporting the failed batch as imported.
- Missing or invalid API key: reject upload through the existing `/api/logs` authorization middleware.

---

## File Structure

- Modify `CentralLogging.Api/Endpoints/LogEndpoints.cs` to share validation/persistence behavior and implement multipart upload parsing and summary results.
- Modify `CentralLogging.Api/Program.cs` to map the upload route under `/api/logs`.
- Modify `CentralLogging.Api/wwwroot/index.html` to add a file upload panel.
- Modify `CentralLogging.Api/wwwroot/log-search.js` to submit multipart data with the in-memory API key and present the result.
- Modify `CentralLogging.Api/wwwroot/log-search.css` for the upload panel layout and feedback.
- Modify `CentralLogging.Api/README.md` to document the accepted format and upload result.

### Task 1: Add authenticated file ingestion endpoint

**Files:**
- Modify: `CentralLogging.Api/Endpoints/LogEndpoints.cs`
- Modify: `CentralLogging.Api/Program.cs`
- Modify: `CentralLogging.Api/README.md`

**Interfaces:**
- Produce `UploadAsync(IFormFile? file, CentralLogsDbContext database, CancellationToken cancellationToken)` as the endpoint handler; return 400 when `file` is missing.
- Return a JSON result with integer `accepted`, `duplicates`, and `invalidLines` values.
- Map `POST /api/logs/upload` and disable antiforgery for this API route; API-key authorization remains supplied by the existing path middleware.

- [x] Extract the current validation and transactional insert/catalog update from `IngestAsync` into a shared batch operation that returns accepted and duplicate counts; preserve `POST /api/logs` response and validation behavior.
- [x] Implement `UploadAsync` to read nonblank lines from the uploaded file, deserialize each as `LogEventInput` with the forwarder-compatible case-insensitive JSON options and `level`-to-`severity` fallback, count malformed or invalid events, and submit valid events in batches of at most `MaximumBatchSize`.
- [x] Ensure a line is included in `invalidLines` only when nonblank and either malformed or invalid; let persistence failures fail the upload request.
- [x] Map `/api/logs/upload` and update the API README to describe newline-delimited JSON, idempotent duplicate handling, and the response counts.
- [x] Review the endpoint against the five Review Focus cases above by tracing each path through parsing and shared ingestion code; do not add or run tests unless requested.

### Task 2: Add upload controls to the UI

**Files:**
- Modify: `CentralLogging.Api/wwwroot/index.html`
- Modify: `CentralLogging.Api/wwwroot/log-search.js`
- Modify: `CentralLogging.Api/wwwroot/log-search.css`

**Interfaces:**
- Consume `POST /api/logs/upload` using `FormData` with a field named `file` and header `X-Log-Api-Key`.
- Display the returned `accepted`, `duplicates`, and `invalidLines` counts as status text.

- [x] Add an upload panel with an accessible `.json`/`.jsonl` file picker, upload button, and live status message; keep the upload button disabled until the API key connection is established and a file is selected.
- [x] Add an API multipart `post` helper or upload-specific fetch call that does not set the multipart `Content-Type` manually, and render upload progress state, errors, and the returned counts.
- [x] Clarify that search results are database-wide and clear previous results after a successful upload until the next search.
- [x] Add responsive styling consistent with the existing panels and verify the UI state transitions by inspecting the event handlers and markup; do not add or run tests unless requested.
