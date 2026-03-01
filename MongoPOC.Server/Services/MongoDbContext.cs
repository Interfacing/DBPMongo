using MongoDB.Driver;
using MongoPOC.Server.Models;

namespace MongoPOC.Server.Services;

public sealed class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDBP");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'MongoDBP' is missing.");
        }

        var databaseName = configuration["MongoDb:DatabaseName"] ?? "MongoDBP";
        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<MongoDocumentRecord> GetCollection(DocumentKind kind)
        => _database.GetCollection<MongoDocumentRecord>(kind.ToCollectionName());

    public IReadOnlyList<(DocumentKind Kind, IMongoCollection<MongoDocumentRecord> Collection)> GetAllCollections()
        => DocumentKindExtensions.All
            .Select(kind => (kind, GetCollection(kind)))
            .ToList();
}
