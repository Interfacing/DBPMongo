# MongoPOC implementation plan

## Problem and approach
- Build a new full-stack project with MongoDB (Docker), ASP.NET backend, .NET Aspire orchestration, and React frontend.
- Ingest JSON files from `JSON\` where each subfolder maps to a distinct MongoDB collection.
- Provide data dashboard + advanced retrieval by keywords and field-level filters.
- Keep ingestion idempotent (upsert by source file path) and preserve full raw JSON per record.

## Current state analysis
- Repository currently contains only input data at `C:\Projects\MongoPOC\JSON`.
- JSON subfolders:
  - `DJSON` (579 files)
  - `FormData` (275 files)
  - `FormDataPreview` (277 files)
- Sample files reviewed from each subfolder:
  - `Admin Application_Admin App EPC Setting_13796b3c-2a98-4fb5-903e-83f09e3de123.json`
  - `Admin Application_Admin App Manual_9cadf2fd-deef-4070-93aa-ceddaf174601.json`
  - `QMS_New CAPA_4bda98ef-f3d2-4f9d-b856-6b046cd68d61.json`
- Observed shapes:
  - `DJSON` documents: top keys mainly `shortname`, `application`, `icon`, `form` (+ occasional `contextId/contextType/processId/ruleGroups`).
  - `FormData` documents: `FormId`, `FormBehaviour`, `InstanceId`, `Title`, `FormTitle`, `Rows`, `FormObject`, rules/validation metadata.
  - `FormDataPreview` documents: same core structure as `FormData` with preview-oriented payload.

## Proposed solution architecture
1. **Aspire AppHost**
   - Orchestrates MongoDB container, backend service, and frontend service.
   - Injects connection strings and service URLs via Aspire configuration.
2. **ASP.NET backend (Web API)**
   - Ingestion endpoints/services for folder scan + file upload.
   - Query endpoints for dashboard stats and filtered search.
   - MongoDB repositories + indexing at startup.
3. **React frontend**
   - Dashboard page for counts/trends per collection.
   - Search page for keyword + field filters.
   - Result details with formatted JSON viewer.
4. **MongoDB (Docker image)**
   - Single database with 3 base collections (by subfolder).
   - Optional audit collection for ingestion runs/logs.
   - Persisted in Aspire using named volume `mongopoc-mongodb-data`.
5. **MongoDB Compass access**
   - Added to Aspire as a `mongodb-compass` resource via Mongo Express for local DB inspection.

## Collection and document strategy
- Database: `MongoDBP`.
- Collections:
  - `djson_documents` (from `JSON\DJSON`)
  - `form_data_documents` (from `JSON\FormData`)
  - `form_data_preview_documents` (from `JSON\FormDataPreview`)
- Store each ingested item as:
  - `source`: `{ folder, fileName, relativePath, hash, ingestedAtUtc }`
  - `kind`: one of `DJSON|FormData|FormDataPreview`
  - `normalized`: common searchable fields (`formId`, `formName`, `title`, `application`, `shortname`, `instanceId`)
  - `searchText`: flattened keyword text for text index
  - `raw`: original JSON document (unchanged)
- Idempotency key: `source.relativePath` (or `source.relativePath + hash` if versioning is needed).

## Indexing plan
- Unique index: `source.relativePath`.
- Filter indexes:
  - `kind`
  - `normalized.formId`
  - `normalized.formName`
  - `normalized.application`
  - `normalized.shortname`
  - `normalized.instanceId`
- Text index: `searchText` for keyword retrieval.
- Optional compound indexes after first profiling pass (based on actual query usage).

## Backend API plan
- `POST /api/ingestion/scan`
  - Scans `JSON\` recursively and ingests all files by subfolder mapping.
- `POST /api/ingestion/upload`
  - Accepts JSON file(s), target kind/subfolder, ingests via same pipeline.
- `GET /api/dashboard/summary`
  - Counts by collection, recent ingested files, top forms/apps.
- `POST /api/search`
  - Supports:
    - keyword text query
    - collection filter
    - exact filters (`formId`, `formName`, `application`, `instanceId`)
    - dynamic field filter (`fieldPath` + exact `value`) over `raw`/`normalized` for v1.
- `GET /api/documents/{id}`
  - Returns normalized metadata + raw JSON.

## Frontend plan (React)
- **Dashboard**
  - Cards for total docs + per-collection counts.
  - Table/charts for recent ingestions and top form names.
- **Search**
  - Inputs: keyword, collection dropdown, form/app fields, dynamic field path/value.
  - Server-side pagination + sorting.
- **Document view**
  - Metadata panel + expandable raw JSON viewer.
  - Quick copy of matched field paths/values.

## Implementation todos
- `bootstrap-solution`: Scaffold solution structure (AppHost, Api, Frontend, shared config).
- `wire-aspire-mongodb`: Configure Aspire to run MongoDB container and pass connection to API.
- `define-data-models`: Add normalized ingestion models and Mongo collection abstractions.
- `build-ingestion-pipeline`: Implement scan/upload ingestion, subfolder->collection routing, hashing/upsert.
- `create-mongodb-indexes`: Add startup index creation for unique/filter/text indexes.
- `build-query-service`: Implement dashboard aggregation + search service with keyword and field-path filtering.
- `expose-api-endpoints`: Add ingestion/dashboard/search/document endpoints with request/response contracts.
- `build-react-ui`: Implement dashboard, search form, result grid, and JSON details viewer.
- `integrate-frontend-backend`: Add API client layer, environment wiring, and Aspire frontend registration.
- `validate-with-sample-data`: Run ingestion on all subfolders and validate queries using sampled files.

## Dependency order
1. `bootstrap-solution`
2. `wire-aspire-mongodb`
3. `define-data-models`
4. `build-ingestion-pipeline`
5. `create-mongodb-indexes`
6. `build-query-service`
7. `expose-api-endpoints`
8. `build-react-ui`
9. `integrate-frontend-backend`
10. `validate-with-sample-data`

## Notes and considerations
- Keep raw documents unmodified to preserve traceability.
- Keep normalized projection minimal and derived; avoid losing source semantics.
- Start with collection-per-subfolder mapping as requested; revisit only if query patterns demand finer partitioning.
- Use structured logs for ingestion stats (processed, skipped, failed) and expose in dashboard later if needed.
- Confirmed scope decision: specific-field search in v1 uses exact-match semantics only.
