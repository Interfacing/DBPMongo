namespace MongoPOC.Server.Models;

public sealed record ScanIngestionRequest(string? RootPath);

public sealed record IngestionSummary(
    int Processed,
    int Upserted,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);

public sealed record CollectionCountItem(string Collection, long Count);

public sealed record DashboardTopItem(string Value, long Count);

public sealed record RecentDocumentItem(
    string Id,
    string Collection,
    string Kind,
    string FileName,
    string RelativePath,
    DateTime IngestedAtUtc,
    string? FormId,
    string? FormName,
    string? Title,
    string? Application,
    string? Shortname,
    string? InstanceId);

public sealed record DashboardSummaryResponse(
    long TotalDocuments,
    IReadOnlyList<CollectionCountItem> CollectionCounts,
    IReadOnlyList<DashboardTopItem> TopForms,
    IReadOnlyList<DashboardTopItem> TopApplications,
    IReadOnlyList<RecentDocumentItem> RecentDocuments);

public sealed class SearchRequest
{
    public string? Keyword { get; set; }

    public string? Collection { get; set; }

    public string? FormId { get; set; }

    public string? FormName { get; set; }

    public string? Title { get; set; }

    public string? Application { get; set; }

    public string? Shortname { get; set; }

    public string? InstanceId { get; set; }

    public FieldFilterRequest? FieldFilter { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

public sealed class FieldFilterRequest
{
    public string FieldPath { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public sealed record SearchDocumentItem(
    string Id,
    string Collection,
    string Kind,
    string FileName,
    string RelativePath,
    DateTime IngestedAtUtc,
    string? FormId,
    string? FormName,
    string? Title,
    string? Application,
    string? Shortname,
    string? InstanceId);

public sealed record SearchResponse(
    long Total,
    int Page,
    int PageSize,
    IReadOnlyList<SearchDocumentItem> Items);

public sealed record DocumentDetailResponse(
    string Id,
    string Collection,
    string Kind,
    SourceMetadata Source,
    NormalizedMetadata Normalized,
    string RawJson);

public sealed record UploadedJsonFile(string FileName, string Content);
