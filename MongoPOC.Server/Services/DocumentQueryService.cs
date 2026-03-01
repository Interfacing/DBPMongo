using System.Globalization;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using MongoPOC.Server.Models;

namespace MongoPOC.Server.Services;

public sealed class DocumentQueryService
{
    private readonly MongoDbContext _mongoDbContext;

    // Excludes the raw JSON blob and full-text search field — not needed for list/dashboard views.
    private static readonly ProjectionDefinition<MongoDocumentRecord> ListProjection =
        Builders<MongoDocumentRecord>.Projection
            .Exclude(r => r.Raw)
            .Exclude(r => r.SearchText);

    public DocumentQueryService(MongoDbContext mongoDbContext)
    {
        _mongoDbContext = mongoDbContext;
    }

    public async Task<DashboardSummaryResponse> GetDashboardSummaryAsync(CancellationToken cancellationToken)
    {
        var collectionPairs = _mongoDbContext.GetAllCollections();

        // Fire count and document queries concurrently across all collections.
        var countTasks = collectionPairs
            .Select(pair =>
            {
                var (_, collection) = pair;
                return collection.CountDocumentsAsync(
                    FilterDefinition<MongoDocumentRecord>.Empty, cancellationToken: cancellationToken);
            })
            .ToList();

        var docTasks = collectionPairs
            .Select(async pair =>
            {
                var (kind, collection) = pair;
                var documents = await collection
                    .Find(FilterDefinition<MongoDocumentRecord>.Empty)
                    .Project<MongoDocumentRecord>(ListProjection)
                    .ToListAsync(cancellationToken);
                return (Kind: kind, Documents: documents);
            })
            .ToList();

        var counts = await Task.WhenAll(countTasks);
        var kindedResults = await Task.WhenAll(docTasks);

        var collectionCounts = collectionPairs
            .Zip(counts, (pair, count) =>
            {
                var (kind, _) = pair;
                return new CollectionCountItem(kind.ToCollectionName(), count);
            })
            .ToList();

        var allDocuments = kindedResults
            .SelectMany(r => r.Documents.Select(d => (r.Kind, Document: d)))
            .ToList();

        var totalDocuments = counts.Sum();
        var topForms = BuildTopItems(allDocuments, n => n.FormName);
        var topApplications = BuildTopItems(allDocuments, n => n.Application);
        var recentDocuments = allDocuments
            .OrderByDescending(item => item.Document.Source.IngestedAtUtc)
            .Take(20)
            .Select(item => ToDocumentListItem(item.Document, item.Kind))
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

        // Fire count and document queries concurrently across all matching collections.
        var countTasks = kinds
            .Select(kind => _mongoDbContext.GetCollection(kind)
                .CountDocumentsAsync(filter, cancellationToken: cancellationToken))
            .ToList();

        var docTasks = kinds
            .Select(async kind =>
            {
                var documents = await _mongoDbContext.GetCollection(kind)
                    .Find(filter)
                    .Project<MongoDocumentRecord>(ListProjection)
                    .ToListAsync(cancellationToken);
                return (Kind: kind, Documents: documents);
            })
            .ToList();

        var counts = await Task.WhenAll(countTasks);
        var kindedResults = await Task.WhenAll(docTasks);

        var total = counts.Sum();
        var items = kindedResults
            .SelectMany(r => r.Documents.Select(d => (r.Kind, Document: d)))
            .OrderByDescending(item => item.Document.Source.IngestedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => ToDocumentListItem(item.Document, item.Kind))
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

    private static List<DashboardTopItem> BuildTopItems(
        IEnumerable<(DocumentKind Kind, MongoDocumentRecord Document)> source,
        Func<NormalizedMetadata, string?> selector,
        int take = 10) =>
        source
            .Select(item => selector(item.Document.Normalized))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(g => new DashboardTopItem(g.Key, g.Count()))
            .ToList();

    private static DocumentListItem ToDocumentListItem(MongoDocumentRecord document, DocumentKind kind) =>
        new(document.Id.ToString(),
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
