# MongoPOC — MongoDB Document Dashboard

A proof-of-concept application for ingesting, storing, and searching JSON documents in MongoDB. Built with .NET Aspire, ASP.NET, and React.

**Repository:** https://github.com/Interfacing/DBPMongo

## Architecture

```
MongoPOC.AppHost          — .NET Aspire orchestrator (MongoDB, Mongo Express, backend, frontend)
MongoPOC.Server           — ASP.NET minimal API (ingestion, search, dashboard)
frontend/                 — React + Vite SPA (dashboard, search UI, document viewer)
JSON/                     — Source JSON files organized by type (DJSON, FormData, FormDataPreview)
```

Aspire provisions a **MongoDB 8.x** container with a persistent named volume (`mongopoc-mongodb-data`) and a **Mongo Express** UI on port 8081 for direct database inspection.

## Collections

Each subfolder under `JSON/` maps to a separate MongoDB collection:

| Folder             | Collection                     |
|--------------------|--------------------------------|
| `DJSON`            | `djson_documents`              |
| `FormData`         | `form_data_documents`          |
| `FormDataPreview`  | `form_data_preview_documents`  |

Documents are stored with normalized metadata (formId, formName, title, application, shortname, instanceId), a full-text `searchText` field, and the original JSON in `raw`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

## Quick start

```bash
# Clone the repository
git clone https://github.com/Interfacing/DBPMongo.git
cd DBPMongo

# Install frontend dependencies
cd frontend && npm install && cd ..

# Run the full stack via Aspire
dotnet run --project MongoPOC.AppHost
```

The Aspire dashboard URL is printed in the console output. From there you can access the backend, frontend, and Mongo Express resources.

## Build & lint

```bash
# Backend
dotnet build MongoPOC.sln

# Frontend
cd frontend
npm run lint
npm run build
```

## API endpoints

All routes are under `/api`.

| Method | Path                     | Description                                      |
|--------|--------------------------|--------------------------------------------------|
| POST   | `/api/ingestion/scan`    | Scan `JSON/` folder and ingest all files          |
| POST   | `/api/ingestion/upload`  | Upload JSON files via multipart form (`kind` + `files`) |
| GET    | `/api/dashboard/summary` | Collection counts, top forms/apps, recent docs   |
| POST   | `/api/search`            | Search by keyword, normalized fields, or raw field path |
| GET    | `/api/documents/{id}`    | Retrieve full document with raw JSON             |

### Search request body

```json
{
  "keyword": "CAPA",
  "collection": "FormData",
  "formId": null,
  "formName": null,
  "application": null,
  "fieldFilter": { "fieldPath": "form.id", "value": "some-guid" },
  "page": 1,
  "pageSize": 25
}
```

## Running without Aspire

If you need to run the backend directly (e.g. for debugging):

```bash
# Start MongoDB manually
docker run -d -p 27017:27017 mongo:8.0

# Set connection string and run
$env:ConnectionStrings__MongoDBP='mongodb://localhost:27017'
dotnet run --project MongoPOC.Server
```

## Project structure

```
MongoPOC.AppHost/
  AppHost.cs                 – Aspire resource wiring (Mongo, Express, Server, Vite)

MongoPOC.Server/
  Program.cs                 – Minimal API endpoints
  Models/
    DocumentKind.cs          – Enum mapping subfolders → collections
    MongoDocumentRecord.cs   – BSON-mapped document model
    ApiContracts.cs          – Request/response DTOs
  Services/
    MongoDbContext.cs         – MongoDB client + collection access
    MongoIndexService.cs     – Index creation on startup
    JsonIngestionService.cs  – Folder scan, file upload, upsert logic
    DocumentQueryService.cs  – Dashboard aggregation, search, document retrieval

frontend/
  src/App.tsx                – Single-page dashboard, search, and document viewer

JSON/                        – Source JSON files (DJSON, FormData, FormDataPreview)
```
