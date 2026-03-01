# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Full stack — primary way to run (MongoDB + Mongo Express + backend + frontend)
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

Three runtime components orchestrated by .NET Aspire (.NET 10):

1. **MongoPOC.AppHost** — Aspire orchestrator (`AppHost.cs`). Provisions MongoDB 8.x with a persistent volume (`mongopoc-mongodb-data`), Mongo Express on port 8081, the ASP.NET backend, and the Vite frontend via `AddViteApp()`. All resource wiring is in one file.

2. **MongoPOC.Server** — ASP.NET minimal API. All endpoints are defined inline in `Program.cs` under `/api`. No controllers. Services registered as singletons:
   - `MongoDbContext` — MongoDB client + typed collection accessors
   - `MongoIndexService` — creates indexes at startup
   - `JsonIngestionService` — folder scan, file upload, SHA-256 dedup, upsert logic
   - `DocumentQueryService` — dashboard aggregations, search, document retrieval

3. **frontend/** — React 19 + Vite 7 SPA. The entire UI lives in `src/App.tsx` (single-file). Vite proxies `/api` to the backend via `SERVER_HTTPS`/`SERVER_HTTP` env vars injected by Aspire.

### Data flow

```
JSON/ subfolders → JsonIngestionService → MongoDB collections → DocumentQueryService → API → React UI
```

- `JSON/DJSON` → `djson_documents`
- `JSON/FormData` → `form_data_documents`
- `JSON/FormDataPreview` → `form_data_preview_documents`

Each document in all collections uses the same `MongoDocumentRecord` shape:
- `source` — file origin (folder, fileName, relativePath, SHA-256 hash, ingestedAtUtc)
- `kind` — string enum (DJSON | FormData | FormDataPreview)
- `normalized` — extracted fields: formId, formName, title, application, shortname, instanceId
- `searchText` — full flattened text for MongoDB text index
- `raw` — original JSON stored as BsonDocument

Idempotency key is `source.relativePath`; upsert replaces on match.

### Adding a new JSON document type

1. Add a new value to `DocumentKind` enum in `Models/DocumentKind.cs` — this is the central mapping between subfolder name, collection name, and enum value.
2. Update `BuildNormalizedMetadata()` in `JsonIngestionService` to extract relevant fields.
3. MongoDB indexes are created automatically by `MongoIndexService` at startup.

## Key Conventions

### MongoDB Driver v3 (3.5.1)

`ObjectId` defaults to `ObjectId("000000000000000000000000")` — always set `Id = ObjectId.GenerateNewId()` explicitly on new records, or reuse the existing ID on upsert.

### Aspire connection strings

Aspire auto-injects `ConnectionStrings:MongoDBP`. For standalone running, set `ConnectionStrings__MongoDBP` as an env var (double underscore for nested config).

### JSON source path resolution

`JsonIngestionService.ResolveJsonRootPath()` checks candidates in order: explicit parameter → `JSON_SOURCE_PATH` env var → `MongoDb:JsonSourcePath` config → relative `../JSON` → relative `JSON`. The AppHost sets `JSON_SOURCE_PATH` to `..\JSON` relative to its own directory.

### TypeScript

`tsconfig.app.json` enables `verbatimModuleSyntax`. Type-only imports must use `import type { X }` syntax, separate from value imports.

### Multipart upload

File upload in the minimal API uses `HttpRequest` + `ReadFormAsync()` directly — `[FromForm]` binding doesn't work reliably with minimal APIs for multipart file uploads.

### frontend.esproj excluded from solution

`frontend/frontend.esproj` is intentionally excluded from `MongoPOC.sln`. Adding it causes "no runner found for execution type 'IDE'" errors from the CLI. Aspire manages the frontend via `AddViteApp()`.

## API Reference

All routes under `/api`:

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/ingestion/scan` | Scan `JSON/` folder and ingest all files |
| POST | `/api/ingestion/upload` | Upload JSON via multipart (`kind` + `files`) |
| GET | `/api/dashboard/summary` | Collection counts, top forms/apps, recent docs |
| POST | `/api/search` | Search by keyword, normalized fields, or raw field path |
| GET | `/api/documents/{id}` | Full document with raw JSON |

Search request body fields: `keyword`, `collection`, `formId`, `formName`, `application`, `fieldFilter` (`{ fieldPath, value }`), `page`, `pageSize`.
