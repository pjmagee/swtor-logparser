# External Integrations

**Analysis Date:** 2026-06-11

## APIs & External Services

**None.**
- This application performs no network/HTTP calls. There are no `HttpClient`, REST, GraphQL, or SDK-based service integrations anywhere in the codebase.
- The only "integration" is with the locally installed game (Star Wars: The Old Republic) via files it writes to disk.

## Data Storage

**Databases:**
- None. No database, ORM, or connection strings.

**File Storage:**
- Local filesystem only. Two read-only sources, resolved at runtime in `SwtorLogParser/Monitor/CombatLogs.cs`:
  - Combat logs: `%MyDocuments%\Star Wars - The Old Republic\CombatLogs\*.txt`
    (`Environment.SpecialFolder.MyDocuments`).
  - Player settings: `%LocalAppData%\SWTOR\swtor\settings\*PlayerGUIState.ini`
    (`Environment.SpecialFolder.LocalApplicationData`) - parsed to extract player names.
- Files are opened read-only with `FileShare.ReadWrite` so the game can keep writing while the parser tails the log (`SwtorLogParser/Monitor/CombatLogsMonitor.cs:158`).

**Caching:**
- In-memory only. Static dictionaries `ActionCache` and `GameObjectCache` in `SwtorLogParser/Monitor/CombatLogs.cs` cache parsed log objects to reduce allocations. No external cache.

## Authentication & Identity

**Auth Provider:**
- None. No authentication, authorization, or identity handling. The app is a local desktop utility with no accounts or sessions.

## Monitoring & Observability

**Error Tracking:**
- None. No Sentry/Application Insights or equivalent.

**Logs:**
- Microsoft.Extensions.Logging with Console and Debug providers referenced in the core library (`SwtorLogParser.csproj`). Used for local diagnostic logging only; no log shipping.

## CI/CD & Deployment

**Hosting:**
- None. Distributed as standalone Windows executables (`swtorlogparser.cli.exe`, `swtorlogparser.native.cli.exe`, `SwtorLogParser.Overlay.exe`). See `README.md`.

**CI Pipeline:**
- None detected. No `.github/workflows`, Azure Pipelines, or other CI config in the repo.

## Environment Configuration

**Required env vars:**
- None. Paths are derived from OS special folders, not environment variables.

**Secrets location:**
- None. No secrets, tokens, or credentials are used or stored.

## Webhooks & Callbacks

**Incoming:**
- None.

**Outgoing:**
- None.

## OS / Native Integrations

**Windows P/Invoke (Overlay only):**
- `user32.dll` via `[DllImport]` in `SwtorLogParser.Overlay/NativeMethods.cs` - `SendMessage` and `ReleaseCapture` used to implement borderless-window dragging for the overlay.

**Filesystem polling:**
- `CombatLogsMonitor` polls the combat logs directory on a loop (`Task.Delay(2s)` for new-file detection, `Task.Delay(5s)` reconnect backoff) rather than using `FileSystemWatcher`. See `SwtorLogParser/Monitor/CombatLogsMonitor.cs`.

---

*Integration audit: 2026-06-11*
