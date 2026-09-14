# Smart-X IoT Mesh Gateway — Part 1: Data Ingestion & Validation Gateway

A hybrid IoT ingestion gateway for the Smart-X ecosystem (hydroponic farms, automated real-estate utility trackers, smart grid installations). Built with **ASP.NET Core
Minimal API + SignalR** on the backend and **Blazor WebAssembly** on the frontend,
sharing a single C# class library between both ends.

## Architecture

```
SmartX.sln
├── SmartX.Shared/     Domain models, custom data structures, algorithms (shared by both ends)
│   ├── Models/         TelemetryPacket<T>, PowerReading, DeploymentNode, SensorProfile, DTOs
│   ├── Collections/     TelemetryHistory<T>  — custom timestamp-keyed Binary Search Tree
│   └── Services/        BatchGenerator (jagged arrays), FileEncryptionService (AES-256)
├── SmartX.Api/         ASP.NET Core Minimal API — ingestion endpoints + SignalR hub
└── SmartX.Client/      Blazor WebAssembly dashboard
```

### Why this stack
Both the API and the client reference `SmartX.Shared`, so the sensor models, the
generic `TelemetryPacket<T>`, and the operator-overloaded `PowerReading` type are
defined **once** and used identically on both sides — no duplicate modelling, no
drift, and the whole solution builds/runs as a single coherent unit.

### Where each rubric-mapped concept lives
| Concept | File |
|---|---|
| Generics (no boxing) | `SmartX.Shared/Models/TelemetryPacket.cs` |
| Operator overloading | `SmartX.Shared/Models/PowerReading.cs` |
| Jagged arrays → List\<T\> | `SmartX.Shared/Services/BatchGenerator.cs` |
| Recursion (deployment tree) | `SmartX.Shared/Models/DeploymentNode.cs` — `ValidatePath` |
| Recursion (BST insert/search) | `SmartX.Shared/Collections/TelemetryHistory.cs` |
| Custom collection (not List/Dictionary) | `SmartX.Shared/Collections/TelemetryHistory.cs` |
| Encrypted file upload | `SmartX.Shared/Services/FileEncryptionService.cs` + `POST /api/sensors/{id}/attachments` |
| Real-time engagement strategy | `SmartX.Client/Pages/Ingestion.razor` — live SignalR anomaly-pulse feed |

## Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- (Optional) Docker Desktop, if you want to run via containers instead

## Setup & Run — Local (no Docker)

1. **Restore dependencies** (from the solution root):
   ```bash
   dotnet restore
   ```

2. **Boot the backend API** (Terminal 1):
   ```bash
   cd SmartX.Api
   dotnet run
   ```
   The API starts at `http://localhost:5050`. Confirm it's alive:
   ```bash
   curl http://localhost:5050/api/status
   ```

3. **Run the Blazor client** (Terminal 2, separate terminal — leave the API running):
   ```bash
   cd SmartX.Client
   dotnet run
   ```
   Open the URL shown in the console (typically `http://localhost:5050` for the
   client's own dev server port — check the terminal output for the exact port,
   e.g. `http://localhost:7050` or similar). Navigate to **Sensor Data Ingestion**
   from the landing page.

> If the client's dev-server port differs from `7050`/`5050` used in `Program.cs`
> CORS policy, update the `WithOrigins(...)` list in `SmartX.Api/Program.cs`
> to match, or run `dotnet run --urls http://localhost:7050`.

## Setup & Run — Docker

```bash
docker-compose up --build
```
- API: `http://localhost:5050`
- Client: `http://localhost:8080`

## Using the Dashboard

1. **Register Sensor** — pick a deployment path (validated recursively against the
   Facility → Zone → Sub-Zone tree) and a category (Environmental/Power/Actuator).
2. **Attach File** — select a sensor, upload a config/log/photo; it's AES-256
   encrypted at rest under `SmartX.Api/wwwroot/uploads`.
3. **Generate Telemetry** — send a single reading, or generate a batch (built as a
   jagged array of raw samples per sensor, then flattened into `List<T>` and pushed
   live via SignalR).
4. **Search Historical Data** — runs a range query against the sensor's custom BST
   (`TelemetryHistory<T>`), reporting the search time in microseconds.
5. **Run Performance Test** — inserts N synthetic readings and reports average
   insert time + a mid-range search, demonstrating how the BST scales.
6. **Live Telemetry Feed** — readings pulse red the instant an anomalous value
   (or valid range breach) arrives, with no polling.

## API Endpoints (summary)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/status` | API health + live counters |
| GET | `/api/sensors` | List registered sensors |
| POST | `/api/sensors/register` | Register a sensor (recursively validates deployment path) |
| POST | `/api/sensors/{id}/attachments` | Encrypted multipart file upload |
| POST | `/api/telemetry/ingest` | Ingest a single typed reading |
| POST | `/api/telemetry/batch` | Simulate + ingest a jagged-array batch |
| GET | `/api/telemetry/history/{id}` | BST range search |
| POST | `/api/telemetry/performance-test/{id}/{count}` | Bulk insert/search timing |
| POST | `/api/deployment/validate` | Recursive path validation |
| Hub | `/hubs/telemetry` | SignalR real-time push |

## GitHub Workflow

This repository is committed incrementally (25+ commits) reflecting the actual
build order: shared models → custom collection → API endpoints → Blazor dashboard →
SignalR live feed → encryption → docs. See commit history for the full trail.

## Part 1 Research Report
See `docs/Research_Report.docx` for the 5 engagement strategies considered and the
500-word justification of the live anomaly-pulse feed chosen for this build.
