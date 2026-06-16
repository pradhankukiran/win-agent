# WinAgent.FileSystemWatcher

A .NET file system watcher that monitors directories, detects file copies, scores event severity, persists events to SQLite, and exposes them via HTTP.

[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## What it does

Runs as a console app. Watches configured directories. Emits events for `Created`, `Deleted`, `Renamed`, and `Changed`. Filters noise, deduplicates rapid changes, flags possible copies with SHA-256 comparison, assigns severity, writes to SQLite, and serves a small HTTP API.

## Features

- **File system monitoring** — `Created`, `Deleted`, `Renamed`, `Changed` events.
- **Filtering** — include/exclude extensions and glob ignore patterns.
- **Deduplication** — collapses `Changed` bursts on the same file within 500 ms.
- **Copy detection** — SHA-256 hash comparison against recently seen files.
- **Async hashing** — hashing runs off the watcher callback thread.
- **Severity scoring** — `Low` / `Medium` / `High` / `Critical`.
- **SQLite persistence** — every event stored locally with pending/retry flag.
- **Replay loop** — retries pending events every 10 seconds.
- **HTTP API** — `/health`, `/status`, `/events`, `/events/stream` (SSE).
- **CLI overrides** — config path, watch paths, JSON output, API port.

## Architecture

```
Watcher Service -> Pipeline (filter/dedup/severity/hash) -> Console / JSON
                                    |
                                    v
                          SQLite Cache + Replay
                                    |
                                    v
                              Status API
```

## Build

Requires .NET 10 SDK.

```bash
cd FileSystemWatcher
dotnet build
```

## Run

```bash
dotnet run
```

Override paths and port:

```bash
dotnet run -- --paths /tmp/dlpwatch --port 5001
```

## CLI Flags

| Flag | Description |
|------|-------------|
| `--config <path>` | Alternate `appsettings.json`. |
| `--paths <p1>;<p2>` | Override watched paths. |
| `--json` | Output events as JSON lines. |
| `--port <number>` | HTTP port. Default `5001`. |
| `--help` | Show help. |

## Configuration (`appsettings.json`)

| Setting | Description |
|---------|-------------|
| `WatchedPaths` | Directories to watch. |
| `IncludeSubdirectories` | Recursive watch. |
| `EnableCopyHeuristic` | Enable SHA-256 copy detection. |
| `BatchFlushIntervalSeconds` | Event flush interval. |
| `BatchSize` | Immediate flush threshold. |
| `CopyHeuristicMaxFiles` | Cache size limit. |
| `IncludeExtensions` | Only these extensions. Empty = all. |
| `ExcludeExtensions` | Ignore these extensions. |
| `IgnorePatterns` | Glob patterns to ignore. |
| `BusinessHours` | `{ "Start": "09:00", "End": "18:00" }`. Outside = `High`. |
| `StatusApi:Port` | HTTP port. |

## HTTP Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /health` | `{ "status": "OK" }` |
| `GET /status` | Paths, uptime, event counts. |
| `GET /events?count=50` | Last N events from SQLite. |
| `GET /events/stream` | SSE stream of new events. |

## Example Output

```
[Watcher] Started watching: /tmp/dlpwatch
[2026-06-16 12:34:56] Created  Low      /tmp/dlpwatch/report.docx
[2026-06-16 12:35:05] Created  Medium   /tmp/dlpwatch/backup/report.docx [COPY? source: /tmp/dlpwatch/report.docx]
```

JSON mode:

```json
{"eventType":"Created","fullPath":"/tmp/dlpwatch/report.docx","severity":"Low","pending":false,"timestamp":"2026-06-16T12:34:56+00:00"}
```

## Severity Rules

| Severity | Trigger |
|----------|---------|
| Low | Default event severity. |
| Medium | Copy heuristic match. |
| High | >10 events in 1 second on same path. |
| High | Activity outside `BusinessHours`. |
| Critical | Reserved. |

## Development

```bash
cd FileSystemWatcher

dotnet build
dotnet run -- --paths /tmp/dlpwatch --json --port 5001

# Inspect SQLite cache
sqlite3 events.db "SELECT EventType, FullPath, Severity, Pending FROM FileSystemEvents;"
```

## Notes

- The backend push in this PoC randomly fails ~10% of the time to exercise the SQLite retry loop. Replace `BackendPushService.PushAsync` for production.
- Designed to build and run on Linux for testing; paths in `appsettings.json` should use Windows conventions on Windows.

## License

MIT
