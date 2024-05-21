using Microsoft.Extensions.Hosting;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var dbPassword = builder.AddParameter("PostgresPassword", secret: true);

var backendDb = builder
    .AddPostgres("eshopsupport-postgres", password: dbPassword)
    .WithDataVolume()
    .AddDatabase("backenddb");

var vectorDb = builder
    .AddContainer("vector-db", "qdrant/qdrant", "latest")
    .WithVolume("eshopsupport-vector-db-storage", "/qdrant/storage")
    .WithHttpEndpoint(port: 62392, targetPort: 6333);

var llmModelName = builder.Configuration["LlmModelName"]!;
var ollama = builder.AddOllama("eshopsupport-ollama", models: [llmModelName])
       .WithDataVolume();

var storage = builder.AddAzureStorage("eshopsupport-storage");
if (builder.Environment.IsDevelopment())
{
    storage.RunAsEmulator(r => r.WithDataVolume());
}

var blobStorage = storage.AddBlobs("eshopsupport-blobs");

var backend = builder.AddProject<Backend>("backend")
    .WithReference(backendDb)
    .WithReference(ollama)
    .WithReference(blobStorage)
    .WithReference(vectorDb.GetEndpoint("http"))
    .WithEnvironment("ImportInitialDataDir", Path.Combine(builder.AppHostDirectory, "..", "..", "seeddata", "dev"));

builder.AddProject<StaffWebUI>("staffwebui")
    .WithExternalHttpEndpoints()
    .WithReference(backend)
    .WithReference(ollama);

builder.Build().Run();
