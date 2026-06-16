# WinAgent

A lightweight Windows endpoint agent for Data Loss Prevention (DLP) and Insider Risk Detection.

[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Overview

`WinAgent` is a proof-of-concept endpoint monitoring agent that watches file system activity on a Windows machine, assigns risk severity, persists events locally, and exposes a small HTTP API for dashboards and external integrations.

> **Note:** This repository is currently a PoC. It runs the file system watcher as a console application. Future phases will add a Windows Service host, USB monitoring, browser URL tracking, screenshot detection, and a full backend integration.

## Features

- **File system monitoring** — watches configured directories for `Created`, `Deleted`, `Renamed`, and `Changed` events.
- **Smart filtering** — include/exclude file extensions and ignore glob patterns (e.g., `~$*`, `*.tmp`).
- **Deduplication** — collapses rapid `Changed` bursts on the same file within 500 ms.
- **Copy detection** — compares SHA-256 hashes of new files against recently seen files to flag possible copies.
- **Async processing** — hashing and severity rules run off the watcher callback thread.
- **Severity scoring** — `Low` / `Medium` / `High` / `Critical` based on copy matches, burst rate, business hours, and USB activity stubs.
- **SQLite persistence** — every event is stored locally with a pending/retry mechanism.
- **Replay loop** — pending events are retried every 10 seconds when backend push fails.
- **HTTP status API** — health, status, recent events, and an SSE stream.
- **CLI overrides** — config path, watch paths, JSON output, and API port can be passed as arguments.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                       WinAgent.Host                         │
│  (console app today → Windows Service in production)        │
└───────────────────────┬─────────────────────────────────────┘
                        │
    ┌───────────────────┼───────────────────┐
    ▼                   ▼                   ▼
┌─────────┐      ┌─────────────┐     ┌──────────────┐
│ Watcher │─────▶│   Pipeline  │────▶│   Console    │
│ Service │      │filter/dedup │     │   + JSON     │
└─────────┘      │severity/hash│     └──────────────┘
                 └──────┬──────┘
                        │
        ┌───────────────┼───────────────┐
        ▼               ▼               ▼
┌──────────────┐ ┌─────────────┐ ┌──────────────┐
│  SQLite Cache │ │ Backend Push │ │  Status API  │
│   events.db   │ │  + Replay    │ │  /health     │
└──────────────┘ └─────────────┘ │  /events     │
                                 │  /events/... │
                                 └──────────────┘
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Build

```bash
cd FileSystemWatcher
dotnet build
```

### Run

```bash
dotnet run
```

The default configuration watches `C:\Temp\DlpWatch` on Windows. For Linux testing, override the path:

```bash
dotnet run -- --paths /tmp/dlpwatch --port 5001
```

## CLI Flags

| Flag | Description |
|------|-------------|
| `--config <path>` | Path to an alternate `appsettings.json`. |
| `--paths <p1>;<p2>` | Override `WatchedPaths` (semicolon-separated). |
| `--json` | Output each event as a single JSON line to stdout. |
| `--port <number>` | HTTP status endpoint port (default `5001`). |
| `--help` | Show usage help. |

Example:

```bash
dotnet run -- --paths /tmp/dlpwatch;/tmp/other --json --port 8080
```

## Configuration (`appsettings.json`)

### `Watcher`

| Setting | Description |
|---------|-------------|
| `WatchedPaths` | Array of directory paths to monitor. |
| `IncludeSubdirectories` | Whether to watch subdirectories recursively. |
| `EnableCopyHeuristic` | Enables SHA-256 comparison to flag possible file copies. |
| `BatchFlushIntervalSeconds` | How often to flush queued events. |
| `BatchSize` | Maximum queue size before an immediate flush. |
| `CopyHeuristicMaxFiles` | Maximum number of recent files to keep in the copy heuristic cache. |
| `IncludeExtensions` | Only watch files with these extensions. Example: `[".txt", ".docx"]`. |
| `ExcludeExtensions` | Ignore files with these extensions. Example: `[".tmp", ".log"]`. |
| `IgnorePatterns` | Glob patterns to ignore. Example: `["~$*", "*.tmp"]`. |
| `BusinessHours` | `{ "Start": "09:00", "End": "18:00" }`. Activity outside this window is upgraded to `High` severity. |

### `StatusApi`

| Setting | Description |
|---------|-------------|
| `Port` | Port for the HTTP status endpoint (default `5001`). |

## HTTP Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Returns `200 OK` with `{ "status": "OK" }`. |
| `GET /status` | Watched paths, uptime, total/pending event counts, and configured port. |
| `GET /events?count=50` | Last N events from SQLite (default 50). |
| `GET /events/stream` | Server-Sent Events stream of new events as JSON. Use `curl -N` for live output. |

Examples:

```bash
curl http://localhost:5001/health
curl http://localhost:5001/status
curl "http://localhost:5001/events?count=10"
curl -N http://localhost:5001/events/stream
```

## Example Output

Human-readable:

```
[Watcher] Started watching: /tmp/dlpwatch
[2026-06-16 12:34:56] Created  Low      /tmp/dlpwatch/report.docx
[2026-06-16 12:34:58] Changed  Low      /tmp/dlpwatch/report.docx
[2026-06-16 12:35:01] Renamed  Low      /tmp/dlpwatch/report-final.docx (from: /tmp/dlpwatch/report.docx)
[2026-06-16 12:35:05] Created  Medium   /tmp/dlpwatch/backup/report.docx [COPY? source: /tmp/dlpwatch/report-final.docx]
```

JSON (`--json`):

```json
{"eventType":"Created","fullPath":"/tmp/dlpwatch/report.docx","severity":"Low","pending":false,"timestamp":"2026-06-16T12:34:56+00:00"}
```

## Severity Rules

| Severity | Trigger |
|----------|---------|
| Low | Default for Created / Deleted / Renamed / Changed. |
| Medium | Copy heuristic match (possible file copy). |
| High | More than 10 events in 1 second for the same file path. |
| High | File activity outside configured `BusinessHours`. |
| Critical | USB activity detected + file activity (stub rule; USB detection not yet implemented). |

## Development & Testing

```bash
cd FileSystemWatcher

# Build
dotnet build

# Run with default config
dotnet run

# Run in JSON mode on a custom path
dotnet run -- --paths /tmp/dlpwatch --json --port 5001

# Inspect local SQLite cache
sqlite3 events.db "SELECT EventType, FullPath, Severity, Pending FROM FileSystemEvents;"
```

## Roadmap

- [ ] Windows Service wrapper
- [ ] USB device connect/disconnect detection
- [ ] Browser URL monitoring via extension
- [ ] Screenshot detection
- [ ] Real backend integration (NestJS)
- [ ] Electron dashboard
- [ ] Risk rule engine with configurable thresholds

## License

MIT
