var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongodb")
    .WithDataVolume("mongopoc-mongodb-data");
mongo.WithMongoExpress(
    mongoExpress => mongoExpress.WithHostPort(8081),
    "mongodb-compass");

var mongoDatabase = mongo.AddDatabase("MongoDBP");

var server = builder.AddProject<Projects.MongoPOC_Server>("server")
    .WithReference(mongoDatabase)
    .WithEnvironment("JSON_SOURCE_PATH", "..\\JSON")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
