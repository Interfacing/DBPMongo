using System.Globalization;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using MongoPOC.Server.Models;

namespace MongoPOC.Server.Services;

public sealed class DocumentQueryService
{
    private readonly MongoDbContext _mongoDbContext;

    public DocumentQueryService(MongoDbContext mongoDbContext)
    {
        _mongoDbContext = mongoDbContext;
    }

    public async Task<DashboardSummaryResponse> GetDashboardSummaryAsync(CancellationToken cancellationToken)
    {
        var allDocuments = new List<(DocumentKind Kind, MongoDocumentRecord Document)>();
        var collectionCounts = new List<CollectionCountItem>();

        foreach (var (kind, collection) in _mongoDbContext.GetAllCollections())
        {
            var documents = await collection
                .Find(FilterDefinition<MongoDocumentRecord>.Empty)
                .ToListAsync(cancellationToken);

            collectionCounts.Add(new CollectionCountItem(kind.ToCollectionName(), documents.Count));
            allDocuments.AddRange(documents.Select(document => (kind, document)));
        }

        var totalDocuments = collectionCounts.Sum(item => item.Count);

        var topForms = allDocuments
            .Select(item => item.Document.Normalized.FormName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(group => new DashboardTopItem(group.Key, group.Count()))
            .ToList();

        var topApplications = allDocuments
            .Select(item => item.Document.Normalized.Application)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(group => new DashboardTopItem(group.Key, group.Count()))
            .ToList();

        var recentDocuments = allDocuments
            .OrderByDescending(item => item.Document.Source.IngestedAtUtc)
            .Take(20)
            .Select(item => ToRecentDocumentItem(item.Document, item.Kind))
            .ToList();

        return new DashboardSummaryResponse(
            totalDocuments,
            collectionCounts,
            topForms,
            topApplications,
            recentDocuments);
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            <= 0 => 25,
            > 100 => 100,
            _ => request.PageSize
        };

        var kinds = ResolveKinds(request.Collection);
        var filter = BuildFilter(request);
        var matches = new List<(DocumentKind Kind, MongoDocumentRecord Document)>();

        foreach (var kind in kinds)
        {
            var collection = _mongoDbContext.GetCollection(kind);
            var documents = await collection
                .Find(filter)
                .ToListAsync(cancellationToken);

            matches.AddRange(documents.Select(document => (kind, document)));
        }

        var ordered = matches
            .OrderByDescending(item => item.Document.Source.IngestedAtUtc)
            .ToList();

        var total = ordered.Count;
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => ToSearchDocumentItem(item.Document, item.Kind))
            .ToList();

        return new SearchResponse(total, page, pageSize, items);
    }

    public async Task<DocumentDetailResponse?> GetDocumentByIdAsync(
        string id,
        string? collection,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out var objectId))
        {
            return null;
        }

        var kinds = ResolveKinds(collection);

        foreach (var kind in kinds)
        {
            var currentCollection = _mongoDbContext.GetCollection(kind);
            var document = await currentCollection
                .Find(record => record.Id == objectId)
                .FirstOrDefaultAsync(cancellationToken);

            if (document is not null)
            {
                return new DocumentDetailResponse(
                    document.Id.ToString(),
                    kind.ToCollectionName(),
                    document.Kind,
                    document.Source,
                    document.Normalized,
                    document.Raw.ToJson(new JsonWriterSettings { Indent = true }));
            }
        }

        return null;
    }

    private static IReadOnlyList<DocumentKind> ResolveKinds(string? collectionOrKind)
    {
        if (string.IsNullOrWhiteSpace(collectionOrKind))
        {
            return DocumentKindExtensions.All;
        }

        if (DocumentKindExtensions.TryParse(collectionOrKind, out var parsedKind))
        {
            return [parsedKind];
        }

        foreach (var kind in DocumentKindExtensions.All)
        {
            if (kind.ToCollectionName().Equals(collectionOrKind, StringComparison.OrdinalIgnoreCase))
            {
                return [kind];
            }
        }

        return DocumentKindExtensions.All;
    }

    private static FilterDefinition<MongoDocumentRecord> BuildFilter(SearchRequest request)
    {
        var filters = new List<FilterDefinition<MongoDocumentRecord>>();
        var builder = Builders<MongoDocumentRecord>.Filter;

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            filters.Add(builder.Text(request.Keyword));
        }

        AddEqFilter(filters, "normalized.formId", request.FormId);
        AddEqFilter(filters, "normalized.formName", request.FormName);
        AddEqFilter(filters, "normalized.title", request.Title);
        AddEqFilter(filters, "normalized.application", request.Application);
        AddEqFilter(filters, "normalized.shortname", request.Shortname);
        AddEqFilter(filters, "normalized.instanceId", request.InstanceId);

        if (request.FieldFilter is { FieldPath.Length: > 0 })
        {
            var fieldPath = NormalizeFieldPath(request.FieldFilter.FieldPath);
            var rawValue = request.FieldFilter.Value ?? string.Empty;
            var parsedValue = ParseToBsonValue(rawValue);

            if (parsedValue.BsonType == BsonType.String)
            {
                filters.Add(new BsonDocumentFilterDefinition<MongoDocumentRecord>(
                    new BsonDocument(fieldPath, parsedValue)));
            }
            else
            {
                var orFilter = new BsonDocument("$or", new BsonArray
                {
                    new BsonDocument(fieldPath, rawValue),
                    new BsonDocument(fieldPath, parsedValue)
                });
                filters.Add(new BsonDocumentFilterDefinition<MongoDocumentRecord>(orFilter));
            }
        }

        return filters.Count == 0 ? builder.Empty : builder.And(filters);
    }

    private static void AddEqFilter(ICollection<FilterDefinition<MongoDocumentRecord>> filters, string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        filters.Add(new BsonDocumentFilterDefinition<MongoDocumentRecord>(new BsonDocument(path, value.Trim())));
    }

    private static string NormalizeFieldPath(string fieldPath)
    {
        var path = fieldPath.Trim();
        if (path.StartsWith("raw.", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("normalized.", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return $"raw.{path}";
    }

    private static BsonValue ParseToBsonValue(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dateTimeValue))
        {
            return dateTimeValue;
        }

        return value;
    }

    private static SearchDocumentItem ToSearchDocumentItem(MongoDocumentRecord document, DocumentKind kind)
    {
        return new SearchDocumentItem(
            document.Id.ToString(),
            kind.ToCollectionName(),
            document.Kind,
            document.Source.FileName,
            document.Source.RelativePath,
            document.Source.IngestedAtUtc,
            document.Normalized.FormId,
            document.Normalized.FormName,
            document.Normalized.Title,
            document.Normalized.Application,
            document.Normalized.Shortname,
            document.Normalized.InstanceId);
    }

    private static RecentDocumentItem ToRecentDocumentItem(MongoDocumentRecord document, DocumentKind kind)
    {
        return new RecentDocumentItem(
            document.Id.ToString(),
            kind.ToCollectionName(),
            document.Kind,
            document.Source.FileName,
            document.Source.RelativePath,
            document.Source.IngestedAtUtc,
            document.Normalized.FormId,
            document.Normalized.FormName,
            document.Normalized.Title,
            document.Normalized.Application,
            document.Normalized.Shortname,
            document.Normalized.InstanceId);
    }
}
