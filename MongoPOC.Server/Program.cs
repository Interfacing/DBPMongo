using System.Text;
using Microsoft.AspNetCore.Mvc;
using MongoPOC.Server.Models;
using MongoPOC.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<MongoIndexService>();
builder.Services.AddSingleton<JsonIngestionService>();
builder.Services.AddSingleton<DocumentQueryService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var indexService = scope.ServiceProvider.GetRequiredService<MongoIndexService>();
    await indexService.EnsureIndexesAsync();
}

var api = app.MapGroup("/api");

api.MapPost("/ingestion/scan", async (
    [FromBody] ScanIngestionRequest? request,
    JsonIngestionService ingestionService,
    CancellationToken cancellationToken) =>
{
    var result = await ingestionService.ScanAndIngestAsync(request?.RootPath, cancellationToken);
    return Results.Ok(result);
});

api.MapPost("/ingestion/upload", async (
    HttpRequest request,
    JsonIngestionService ingestionService,
    CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);
    var kind = form["kind"].FirstOrDefault();

    if (!DocumentKindExtensions.TryParse(kind, out var parsedKind))
    {
        return Results.BadRequest(new { error = "Invalid kind. Allowed values: DJSON, FormData, FormDataPreview." });
    }

    var files = form.Files;
    if (files is null || files.Count == 0)
    {
        return Results.BadRequest(new { error = "No files uploaded." });
    }

    var uploadedFiles = new List<UploadedJsonFile>(files.Count);
    foreach (var file in files.Where(file => file.Length > 0))
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var content = await reader.ReadToEndAsync(cancellationToken);
        uploadedFiles.Add(new UploadedJsonFile(file.FileName, content));
    }

    var result = await ingestionService.IngestUploadedFilesAsync(parsedKind, uploadedFiles, cancellationToken);
    return Results.Ok(result);
})
.DisableAntiforgery();

api.MapGet("/dashboard/summary", async (
    DocumentQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var result = await queryService.GetDashboardSummaryAsync(cancellationToken);
    return Results.Ok(result);
});

api.MapPost("/search", async (
    [FromBody] SearchRequest request,
    DocumentQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var result = await queryService.SearchAsync(request, cancellationToken);
    return Results.Ok(result);
});

api.MapGet("/documents/{id}", async (
    string id,
    [FromQuery] string? collection,
    DocumentQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var result = await queryService.GetDocumentByIdAsync(id, collection, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapDefaultEndpoints();
app.UseFileServer();

app.Run();
