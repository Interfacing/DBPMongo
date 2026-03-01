import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'

type CollectionCountItem = {
  collection: string
  count: number
}

type DashboardTopItem = {
  value: string
  count: number
}

type SearchDocumentItem = {
  id: string
  collection: string
  kind: string
  fileName: string
  relativePath: string
  ingestedAtUtc: string
  formId?: string
  formName?: string
  title?: string
  application?: string
  shortname?: string
  instanceId?: string
}

type DashboardSummaryResponse = {
  totalDocuments: number
  collectionCounts: CollectionCountItem[]
  topForms: DashboardTopItem[]
  topApplications: DashboardTopItem[]
  recentDocuments: SearchDocumentItem[]
}

type IngestionSummary = {
  processed: number
  upserted: number
  skipped: number
  failed: number
  errors: string[]
}

type SearchResponse = {
  total: number
  page: number
  pageSize: number
  items: SearchDocumentItem[]
}

type DocumentDetailResponse = {
  id: string
  collection: string
  kind: string
  source: {
    folder: string
    fileName: string
    relativePath: string
    hash: string
    ingestedAtUtc: string
  }
  normalized: {
    formId?: string
    formName?: string
    title?: string
    application?: string
    shortname?: string
    instanceId?: string
  }
  rawJson: string
}

type SearchFormState = {
  keyword: string
  collection: string
  formId: string
  formName: string
  title: string
  application: string
  shortname: string
  instanceId: string
  fieldPath: string
  fieldValue: string
}

const defaultSearchState: SearchFormState = {
  keyword: '',
  collection: '',
  formId: '',
  formName: '',
  title: '',
  application: '',
  shortname: '',
  instanceId: '',
  fieldPath: '',
  fieldValue: '',
}

function App() {
  const [summary, setSummary] = useState<DashboardSummaryResponse | null>(null)
  const [search, setSearch] = useState<SearchFormState>(defaultSearchState)
  const [searchResult, setSearchResult] = useState<SearchResponse | null>(null)
  const [selectedDocument, setSelectedDocument] = useState<DocumentDetailResponse | null>(null)
  const [lastIngestion, setLastIngestion] = useState<IngestionSummary | null>(null)
  const [loadingSummary, setLoadingSummary] = useState(false)
  const [loadingSearch, setLoadingSearch] = useState(false)
  const [loadingDocument, setLoadingDocument] = useState(false)
  const [ingesting, setIngesting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const detailsRef = useRef<HTMLElement | null>(null)

  const collectionOptions = useMemo(
    () => [
      { value: '', label: 'All collections' },
      { value: 'DJSON', label: 'DJSON' },
      { value: 'FormData', label: 'FormData' },
      { value: 'FormDataPreview', label: 'FormDataPreview' },
    ],
    [],
  )

  useEffect(() => {
    void loadDashboard()
  }, [])

  useEffect(() => {
    if (selectedDocument)
    {
      detailsRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }, [selectedDocument])

  const loadDashboard = async () => {
    setLoadingSummary(true)
    setError(null)
    try {
      const response = await fetch('/api/dashboard/summary')
      if (!response.ok) {
        throw new Error(`Dashboard request failed (${response.status})`)
      }
      const payload: DashboardSummaryResponse = await response.json()
      setSummary(payload)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load dashboard')
    } finally {
      setLoadingSummary(false)
    }
  }

  const ingestJsonFolder = async () => {
    setIngesting(true)
    setError(null)
    try {
      const response = await fetch('/api/ingestion/scan', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({}),
      })

      if (!response.ok) {
        throw new Error(`Ingestion request failed (${response.status})`)
      }

      const payload: IngestionSummary = await response.json()
      setLastIngestion(payload)
      await loadDashboard()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to ingest JSON')
    } finally {
      setIngesting(false)
    }
  }

  const runSearch = async (event: FormEvent) => {
    event.preventDefault()
    setLoadingSearch(true)
    setError(null)
    setSelectedDocument(null)

    const body = {
      keyword: search.keyword || null,
      collection: search.collection || null,
      formId: search.formId || null,
      formName: search.formName || null,
      title: search.title || null,
      application: search.application || null,
      shortname: search.shortname || null,
      instanceId: search.instanceId || null,
      fieldFilter:
        search.fieldPath.trim() && search.fieldValue.trim()
          ? {
              fieldPath: search.fieldPath.trim(),
              value: search.fieldValue.trim(),
            }
          : null,
      page: 1,
      pageSize: 50,
    }

    try {
      const response = await fetch('/api/search', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })

      if (!response.ok) {
        throw new Error(`Search request failed (${response.status})`)
      }

      const payload: SearchResponse = await response.json()
      setSearchResult(payload)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to run search')
    } finally {
      setLoadingSearch(false)
    }
  }

  const viewDocument = async (item: SearchDocumentItem) => {
    if (!item.id) {
      setError('Document id is missing.')
      return
    }

    setLoadingDocument(true)
    setError(null)
    setSelectedDocument(null)
    try {
      const response = await fetch(`/api/documents/${encodeURIComponent(item.id)}`)
      if (!response.ok) {
        throw new Error(`Document request failed (${response.status})`)
      }

      const payload: DocumentDetailResponse = await response.json()
      setSelectedDocument(payload)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load document')
    } finally {
      setLoadingDocument(false)
    }
  }

  const updateField = (field: keyof SearchFormState, value: string) =>
    setSearch((current) => ({ ...current, [field]: value }))

  return (
    <main className="page">
      <header className="header">
        <div>
          <h1>MongoPOC Dashboard</h1>
          <p>MongoDBP + ASP.NET + .NET Aspire + React</p>
        </div>
        <button type="button" onClick={ingestJsonFolder} disabled={ingesting}>
          {ingesting ? 'Ingesting...' : 'Ingest JSON folder'}
        </button>
      </header>

      {error && <div className="error">{error}</div>}

      <section className="panel">
        <h2>Stored data overview</h2>
        {loadingSummary && <p>Loading dashboard...</p>}
        {!loadingSummary && summary && (
          <>
            <div className="cards">
              <article className="card">
                <h3>Total documents</h3>
                <p>{summary.totalDocuments}</p>
              </article>
              {summary.collectionCounts.map((item) => (
                <article key={item.collection} className="card">
                  <h3>{item.collection}</h3>
                  <p>{item.count}</p>
                </article>
              ))}
            </div>

            <div className="lists">
              <article>
                <h3>Top forms</h3>
                <ul>
                  {summary.topForms.map((item) => (
                    <li key={item.value}>
                      <span>{item.value}</span>
                      <strong>{item.count}</strong>
                    </li>
                  ))}
                  {summary.topForms.length === 0 && <li>No form data yet.</li>}
                </ul>
              </article>
              <article>
                <h3>Top applications</h3>
                <ul>
                  {summary.topApplications.map((item) => (
                    <li key={item.value}>
                      <span>{item.value}</span>
                      <strong>{item.count}</strong>
                    </li>
                  ))}
                  {summary.topApplications.length === 0 && <li>No application data yet.</li>}
                </ul>
              </article>
            </div>
          </>
        )}
      </section>

      {lastIngestion && (
        <section className="panel ingestion">
          <h2>Last ingestion result</h2>
          <p>
            Processed: <strong>{lastIngestion.processed}</strong> | Upserted: <strong>{lastIngestion.upserted}</strong> |
            Skipped: <strong>{lastIngestion.skipped}</strong> | Failed: <strong>{lastIngestion.failed}</strong>
          </p>
          {lastIngestion.errors.length > 0 && (
            <details>
              <summary>Show ingestion errors</summary>
              <ul>
                {lastIngestion.errors.map((entry) => (
                  <li key={entry}>{entry}</li>
                ))}
              </ul>
            </details>
          )}
        </section>
      )}

      <section className="panel">
        <h2>Search JSON documents</h2>
        <form onSubmit={runSearch} className="search-form">
          <label>
            Keyword
            <input value={search.keyword} onChange={(event) => updateField('keyword', event.target.value)} />
          </label>
          <label>
            Collection
            <select value={search.collection} onChange={(event) => updateField('collection', event.target.value)}>
              {collectionOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <label>
            Form ID
            <input value={search.formId} onChange={(event) => updateField('formId', event.target.value)} />
          </label>
          <label>
            Form Name
            <input value={search.formName} onChange={(event) => updateField('formName', event.target.value)} />
          </label>
          <label>
            Title
            <input value={search.title} onChange={(event) => updateField('title', event.target.value)} />
          </label>
          <label>
            Application
            <input value={search.application} onChange={(event) => updateField('application', event.target.value)} />
          </label>
          <label>
            Shortname
            <input value={search.shortname} onChange={(event) => updateField('shortname', event.target.value)} />
          </label>
          <label>
            Instance ID
            <input value={search.instanceId} onChange={(event) => updateField('instanceId', event.target.value)} />
          </label>
          <label>
            Field path (exact)
            <input
              value={search.fieldPath}
              onChange={(event) => updateField('fieldPath', event.target.value)}
              placeholder="raw.form.id or form.id"
            />
          </label>
          <label>
            Field value (exact)
            <input value={search.fieldValue} onChange={(event) => updateField('fieldValue', event.target.value)} />
          </label>
          <div className="search-actions">
            <button type="submit" disabled={loadingSearch}>
              {loadingSearch ? 'Searching...' : 'Search'}
            </button>
            <button type="button" onClick={() => setSearch(defaultSearchState)}>
              Reset
            </button>
          </div>
        </form>
      </section>

      {searchResult && (
        <section className="panel">
          <h2>Search results ({searchResult.total})</h2>
          {loadingDocument && <p>Loading selected JSON...</p>}
          {searchResult.items.length === 0 && <p>No matching documents found.</p>}
          {searchResult.items.length > 0 && (
            <div className="results-table-wrap">
              <table className="results-table">
                <thead>
                  <tr>
                    <th>Collection</th>
                    <th>File</th>
                    <th>Form</th>
                    <th>Application</th>
                    <th>Ingested</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {searchResult.items.map((item) => (
                    <tr key={`${item.collection}-${item.id}`}>
                      <td>{item.collection}</td>
                      <td title={item.relativePath}>{item.fileName}</td>
                      <td>{item.formName ?? item.formId ?? '-'}</td>
                      <td>{item.application ?? item.shortname ?? '-'}</td>
                      <td>{new Date(item.ingestedAtUtc).toLocaleString()}</td>
                      <td>
                        <button type="button" onClick={() => viewDocument(item)} disabled={loadingDocument}>
                          View JSON
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {selectedDocument && (
        <section className="panel" ref={detailsRef}>
          <h2>Document details</h2>
          <p>
            <strong>Collection:</strong> {selectedDocument.collection} | <strong>File:</strong>{' '}
            {selectedDocument.source.fileName}
          </p>
          <pre>{selectedDocument.rawJson}</pre>
        </section>
      )}
    </main>
  )
}

export default App
