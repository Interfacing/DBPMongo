using MongoDB.Driver;
using MongoPOC.Server.Models;

namespace MongoPOC.Server.Services;

public sealed class MongoIndexService
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly ILogger<MongoIndexService> _logger;

    public MongoIndexService(MongoDbContext mongoDbContext, ILogger<MongoIndexService> logger)
    {
        _mongoDbContext = mongoDbContext;
        _logger = logger;
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var indexes = new List<CreateIndexModel<MongoDocumentRecord>>
        {
            new(
                Builders<MongoDocumentRecord>.IndexKeys.Ascending("source.relativePath"),
                new CreateIndexOptions { Unique = true, Name = "ux_source_relative_path" }),
            new(
                Builders<MongoDocumentRecord>.IndexKeys.Ascending(x => x.Kind),
                new CreateIndexOptions { Name = "ix_kind" }),
            new(
                Builders<MongoDocumentRecord>.IndexKeys.Ascending("normalized.formId"),
                new CreateIndexOptions { Name = "ix_normalized_form_id" }),
            new(
                Builders<MongoDocumentRecord>.IndexKeys.Ascending("normalized.formName"),
                new CreateIndexOptions { Name = "ix_normalized_form_name" }),
            new(
                Builders<MongoDocumentRecord>.IndexKeys.Ascending("normalized.application"),
                new CreateIndexOptions { Name = "ix_normalized_application" }),
            new(
                Builders<MongoDocumentRecord>.IndexKeys.Ascending("normalized.shortname"),
                new CreateIndexOptions { Name = "ix_normalized_shortname" }),
            new(
                Builders<MongoDocumentRecord>.IndexKeys.Ascending("normalized.instanceId"),
                new CreateIndexOptions { Name = "ix_normalized_instance_id" }),
            new(
                Builders<MongoDocumentRecord>.IndexKeys.Text(x => x.SearchText),
                new CreateIndexOptions { Name = "ix_text_search_text" })
        };

        var tasks = _mongoDbContext.GetAllCollections().Select(async pair =>
        {
            var (kind, collection) = pair;
            await collection.Indexes.CreateManyAsync(indexes, cancellationToken);
            _logger.LogInformation("Ensured indexes for collection {Collection} ({Kind})", kind.ToCollectionName(), kind);
        });

        await Task.WhenAll(tasks);
    }
}
