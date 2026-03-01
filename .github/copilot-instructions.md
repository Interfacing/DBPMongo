# Copilot Instructions for MongoPOC

## Build & Run

```bash
# Full stack (MongoDB + backend + frontend) via Aspire — this is the primary way to run
dotnet run --project MongoPOC.AppHost

# Backend only (requires MongoDB running separately)
$env:ConnectionStrings__MongoDBP='mongodb://localhost:27017'
dotnet run --project MongoPOC.Server

# Frontend only (requires backend running; Vite proxies /api to backend)
cd frontend && npm install && npm run dev

# Build
dotnet build MongoPOC.sln
cd frontend && npm run build

# Lint frontend
cd frontend && npm run lint
```

There are no automated tests in this project.

## Architecture

This is a .NET Aspire-orchestrated app with three runtime components:

1. **MongoPOC.AppHost** — Aspire orchestrator. Provisions MongoDB (with persistent volume `mongopoc-mongodb-data`), Mongo Express on port 8081, the ASP.NET backend, and the Vite frontend. All resource wiring lives in `AppHost.cs`.

2. **MongoPOC.Server** — ASP.NET minimal API backend. All endpoints are defined inline in `Program.cs` under `/api`. No controllers. Services are registered as singletons.

3. **frontend/** — React 19 + Vite 7 SPA. Single-file app in `App.tsx`. Vite proxies `/api` requests to the backend via `SERVER_HTTPS`/`SERVER_HTTP` env vars (set by Aspire).

### Data flow

```
JSON/ subfolders → JsonIngestionService → MongoDB collections → DocumentQueryService → API → React UI
```

- `JSON/` has three subfolders: `DJSON`, `FormData`, `FormDataPreview`
- Each subfolder maps to one MongoDB collection via `DocumentKind` enum
- Ingestion reads raw JSON, computes SHA-256 hash for dedup, extracts normalized metadata, builds full-text search text, and upserts by `source.relativePath`

### MongoDB document structure

Every document in all three collections uses the same `MongoDocumentRecord` shape:
- `source` — file origin metadata (folder, fileName, relativePath, hash, ingestedAtUtc)
- `kind` — string enum value (DJSON, FormData, FormDataPreview)
- `normalized` — extracted searchable fields (formId, formName, title, application, shortname, instanceId)
- `searchText` — flattened text of entire document for MongoDB text index
- `raw` — original JSON stored as-is as a BsonDocument

### Collection routing

`DocumentKind` enum in `Models/DocumentKind.cs` is the central mapping between subfolder names, collection names, and enum values. When adding a new JSON type, add it here and update `BuildNormalizedMetadata()` in `JsonIngestionService`.

## Key Conventions

### MongoDB Driver v3

This project uses MongoDB.Driver 3.5.1. `ObjectId` defaults to `ObjectId("000000000000000000000000")` — always set `Id = ObjectId.GenerateNewId()` explicitly on new records, or reuse the existing ID on upsert.

### Aspire connection strings

Aspire auto-injects `ConnectionStrings:MongoDBP`. For standalone running, set `ConnectionStrings__MongoDBP` as an environment variable (double underscore for nested config).

### TypeScript verbatimModuleSyntax

`tsconfig.app.json` enables `verbatimModuleSyntax`. Type-only imports must use `import type { X }` syntax, separate from value imports.

### No frontend .esproj in solution

The `frontend.esproj` file exists in the frontend folder but is intentionally excluded from `MongoPOC.sln`. Adding it causes "no runner found for execution type 'IDE'" errors when running from CLI. Aspire handles the frontend via `AddViteApp()`.

### Multipart upload in minimal API

File upload uses `HttpRequest` + `ReadFormAsync()` instead of `[FromForm]` binding, which doesn't work reliably with minimal APIs for multipart file uploads.

### JSON source path resolution

`JsonIngestionService.ResolveJsonRootPath()` checks multiple candidate paths in order: explicit parameter → `JSON_SOURCE_PATH` env var → `MongoDb:JsonSourcePath` config → relative `../JSON` → relative `JSON`. The AppHost sets `JSON_SOURCE_PATH` to `..\\JSON` relative to its own directory.
