using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoPOC.Server.Models;

namespace MongoPOC.Server.Services;

public sealed class JsonIngestionService
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JsonIngestionService> _logger;

    public JsonIngestionService(
        MongoDbContext mongoDbContext,
        IConfiguration configuration,
        ILogger<JsonIngestionService> logger)
    {
        _mongoDbContext = mongoDbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IngestionSummary> ScanAndIngestAsync(string? rootPath, CancellationToken cancellationToken)
    {
        var effectiveRootPath = ResolveJsonRootPath(rootPath);
        if (!Directory.Exists(effectiveRootPath))
        {
            throw new DirectoryNotFoundException($"JSON source folder not found: {effectiveRootPath}");
        }

        var processed = 0;
        var upserted = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var kind in DocumentKindExtensions.All)
        {
            var folderPath = Path.Combine(effectiveRootPath, kind.ToFolderName());
            if (!Directory.Exists(folderPath))
            {
                _logger.LogWarning("Folder missing for kind {Kind}: {FolderPath}", kind, folderPath);
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(folderPath, "*.json", SearchOption.AllDirectories))
            {
                processed++;
                var relativePath = Path.GetRelativePath(effectiveRootPath, filePath);
                var fileName = Path.GetFileName(filePath);

                try
                {
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var result = await IngestDocumentAsync(kind, fileName, relativePath, content, cancellationToken);
                    upserted += result.Upserted;
                    skipped += result.Skipped;
                }
                catch (Exception ex)
                {
                    failed++;
                    var error = $"{relativePath}: {ex.Message}";
                    errors.Add(error);
                    _logger.LogError(ex, "Failed to ingest {RelativePath}", relativePath);
                }
            }
        }

        return new IngestionSummary(processed, upserted, skipped, failed, errors);
    }

    public async Task<IngestionSummary> IngestUploadedFilesAsync(
        DocumentKind kind,
        IReadOnlyList<UploadedJsonFile> files,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        var upserted = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var file in files)
        {
            processed++;
            var relativePath = Path.Combine("Uploads", kind.ToFolderName(), file.FileName);

            try
            {
                var result = await IngestDocumentAsync(kind, file.FileName, relativePath, file.Content, cancellationToken);
                upserted += result.Upserted;
                skipped += result.Skipped;
            }
            catch (Exception ex)
            {
                failed++;
                var error = $"{file.FileName}: {ex.Message}";
                errors.Add(error);
                _logger.LogError(ex, "Failed to ingest uploaded file {FileName}", file.FileName);
            }
        }

        return new IngestionSummary(processed, upserted, skipped, failed, errors);
    }

    private async Task<(int Upserted, int Skipped)> IngestDocumentAsync(
        DocumentKind kind,
        string fileName,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var collection = _mongoDbContext.GetCollection(kind);
        var normalizedPath = relativePath.Replace('/', '\\');
        var hash = ComputeSha256(content);

        var filter = Builders<MongoDocumentRecord>.Filter.Eq("source.relativePath", normalizedPath);
        var existingDocument = await collection
            .Find(filter)
            .Project(x => new ExistingDocumentProjection
            {
                Id = x.Id,
                Hash = x.Source.Hash
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (existingDocument is not null && string.Equals(existingDocument.Hash, hash, StringComparison.Ordinal))
        {
            return (0, 1);
        }

        var raw = BsonDocument.Parse(content);
        var normalized = BuildNormalizedMetadata(raw, kind);
        var searchText = BuildSearchText(raw, normalized);

        var record = new MongoDocumentRecord
        {
            Id = existingDocument?.Id ?? ObjectId.GenerateNewId(),
            Kind = kind.ToKindValue(),
            Source = new SourceMetadata
            {
                Folder = kind.ToFolderName(),
                FileName = fileName,
                RelativePath = normalizedPath,
                Hash = hash,
                IngestedAtUtc = DateTime.UtcNow
            },
            Normalized = normalized,
            SearchText = searchText,
            Raw = raw
        };

        await collection.ReplaceOneAsync(filter, record, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        return (1, 0);
    }

    private string ResolveJsonRootPath(string? rootPath)
    {
        var candidatePaths = new List<string>();

        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            candidatePaths.Add(rootPath);
        }

        var configured = _configuration["JSON_SOURCE_PATH"] ?? _configuration["MongoDb:JsonSourcePath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidatePaths.Add(configured);
        }

        candidatePaths.Add("..\\JSON");
        candidatePaths.Add("JSON");

        foreach (var candidatePath in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            var fromCurrentDirectory = Path.GetFullPath(candidatePath, Directory.GetCurrentDirectory());
            if (Directory.Exists(fromCurrentDirectory))
            {
                return fromCurrentDirectory;
            }

            var fromBaseDirectory = Path.GetFullPath(candidatePath, AppContext.BaseDirectory);
            if (Directory.Exists(fromBaseDirectory))
            {
                return fromBaseDirectory;
            }
        }

        return Path.GetFullPath(candidatePaths[0], Directory.GetCurrentDirectory());
    }

    private static NormalizedMetadata BuildNormalizedMetadata(BsonDocument raw, DocumentKind kind)
    {
        return kind switch
        {
            DocumentKind.DJSON => new NormalizedMetadata
            {
                FormId = TryGetString(raw, "form.id"),
                FormName = TryGetString(raw, "form.name"),
                Title = TryGetString(raw, "form.name"),
                Application = TryGetString(raw, "application"),
                Shortname = TryGetString(raw, "shortname"),
                InstanceId = TryGetString(raw, "contextId")
            },
            DocumentKind.FormData or DocumentKind.FormDataPreview => new NormalizedMetadata
            {
                FormId = TryGetString(raw, "FormId"),
                FormName = TryGetString(raw, "FormTitle"),
                Title = TryGetString(raw, "Title"),
                Application = TryGetString(raw, "ApplicationName"),
                Shortname = TryGetString(raw, "shortname"),
                InstanceId = TryGetString(raw, "InstanceId")
            },
            _ => new NormalizedMetadata()
        };
    }

    private static string BuildSearchText(BsonDocument raw, NormalizedMetadata normalized)
    {
        const int maxLength = 32_000;
        var builder = new StringBuilder();

        AppendToken(builder, normalized.FormId, maxLength);
        AppendToken(builder, normalized.FormName, maxLength);
        AppendToken(builder, normalized.Title, maxLength);
        AppendToken(builder, normalized.Application, maxLength);
        AppendToken(builder, normalized.Shortname, maxLength);
        AppendToken(builder, normalized.InstanceId, maxLength);

        FlattenBsonValue(raw, builder, maxLength);

        return builder.ToString().Trim();
    }

    private static void FlattenBsonValue(BsonValue value, StringBuilder builder, int maxLength)
    {
        if (builder.Length >= maxLength)
        {
            return;
        }

        switch (value.BsonType)
        {
            case BsonType.Document:
                foreach (var element in value.AsBsonDocument.Elements)
                {
                    AppendToken(builder, element.Name, maxLength);
                    FlattenBsonValue(element.Value, builder, maxLength);
                }
                break;

            case BsonType.Array:
                foreach (var item in value.AsBsonArray)
                {
                    FlattenBsonValue(item, builder, maxLength);
                }
                break;

            case BsonType.String:
                AppendToken(builder, value.AsString, maxLength);
                break;

            case BsonType.Boolean:
                AppendToken(builder, value.AsBoolean ? "true" : "false", maxLength);
                break;

            case BsonType.Int32:
                AppendToken(builder, value.AsInt32.ToString(CultureInfo.InvariantCulture), maxLength);
                break;

            case BsonType.Int64:
                AppendToken(builder, value.AsInt64.ToString(CultureInfo.InvariantCulture), maxLength);
                break;

            case BsonType.Double:
                AppendToken(builder, value.AsDouble.ToString(CultureInfo.InvariantCulture), maxLength);
                break;

            case BsonType.Decimal128:
                AppendToken(builder, value.AsDecimal128.ToString(), maxLength);
                break;

            case BsonType.DateTime:
                AppendToken(builder, value.AsBsonDateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), maxLength);
                break;

            default:
                AppendToken(builder, value.ToString(), maxLength);
                break;
        }
    }

    private static void AppendToken(StringBuilder builder, string? token, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(token) || builder.Length >= maxLength)
        {
            return;
        }

        var normalizedToken = token.Trim();
        if (normalizedToken.Length == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        var remaining = maxLength - builder.Length;
        if (normalizedToken.Length <= remaining)
        {
            builder.Append(normalizedToken);
        }
        else
        {
            builder.Append(normalizedToken[..remaining]);
        }
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string? TryGetString(BsonDocument document, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        BsonValue current = document;

        foreach (var segment in segments)
        {
            if (current.BsonType != BsonType.Document)
            {
                return null;
            }

            var currentDocument = current.AsBsonDocument;
            if (!currentDocument.TryGetValue(segment, out current))
            {
                return null;
            }
        }

        return current.BsonType switch
        {
            BsonType.Null => null,
            BsonType.String => current.AsString,
            _ => current.ToString()
        };
    }

    private sealed class ExistingDocumentProjection
    {
        public ObjectId Id { get; init; }

        public string? Hash { get; init; }
    }
}
