using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoPOC.Server.Models;

[BsonIgnoreExtraElements]
public sealed class MongoDocumentRecord
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("source")]
    public SourceMetadata Source { get; set; } = new();

    [BsonElement("kind")]
    public string Kind { get; set; } = string.Empty;

    [BsonElement("normalized")]
    public NormalizedMetadata Normalized { get; set; } = new();

    [BsonElement("searchText")]
    public string SearchText { get; set; } = string.Empty;

    [BsonElement("raw")]
    public BsonDocument Raw { get; set; } = new();
}

[BsonIgnoreExtraElements]
public sealed class SourceMetadata
{
    [BsonElement("folder")]
    public string Folder { get; set; } = string.Empty;

    [BsonElement("fileName")]
    public string FileName { get; set; } = string.Empty;

    [BsonElement("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [BsonElement("hash")]
    public string Hash { get; set; } = string.Empty;

    [BsonElement("ingestedAtUtc")]
    public DateTime IngestedAtUtc { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class NormalizedMetadata
{
    [BsonElement("formId")]
    [BsonIgnoreIfNull]
    public string? FormId { get; set; }

    [BsonElement("formName")]
    [BsonIgnoreIfNull]
    public string? FormName { get; set; }

    [BsonElement("title")]
    [BsonIgnoreIfNull]
    public string? Title { get; set; }

    [BsonElement("application")]
    [BsonIgnoreIfNull]
    public string? Application { get; set; }

    [BsonElement("shortname")]
    [BsonIgnoreIfNull]
    public string? Shortname { get; set; }

    [BsonElement("instanceId")]
    [BsonIgnoreIfNull]
    public string? InstanceId { get; set; }
}
