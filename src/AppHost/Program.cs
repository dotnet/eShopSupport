using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);
builder.Configuration.Sources.Add(new JsonConfigurationSource { Path = "appsettings.Local.json", Optional = true });

var dbPassword = builder.AddParameter("PostgresPassword", secret: true);

var backendDb = builder
    .AddPostgres("eshopsupport-postgres", password: dbPassword)
    .WithDataVolume()
    .AddDatabase("backenddb");

var vectorDb = builder
    .AddContainer("vector-db", "qdrant/qdrant", "latest")
    .WithVolume("eshopsupport-vector-db-storage", "/qdrant/storage")
    .WithHttpEndpoint(port: 62392, targetPort: 6333);

// Use this if you want to use Ollama
var chatCompletion = builder.AddOllama("chatcompletion").WithDataVolume();

// ... or use this if you want to use OpenAI (having also configured the API key in appsettings)
//var chatCompletion = builder.AddConnectionString("chatcompletion");

var storage = builder.AddAzureStorage("eshopsupport-storage");
if (builder.Environment.IsDevelopment())
{
    storage.RunAsEmulator(r => r.WithDataVolume());
}

var blobStorage = storage.AddBlobs("eshopsupport-blobs");

var pythonInference = builder.AddPythonUvicornApp("python-inference",
    Path.Combine("..", "PythonInference"), port: 62394);

var backend = builder.AddProject<Backend>("backend")
    .WithReference(backendDb)
    .WithReference(chatCompletion)
    .WithReference(blobStorage)
    .WithReference(vectorDb.GetEndpoint("http"))
    .WithReference(pythonInference)
    .WithEnvironment("ImportInitialDataDir", Path.Combine(builder.AppHostDirectory, "..", "..", "seeddata", "dev"));

builder.AddProject<StaffWebUI>("staffwebui")
    .WithExternalHttpEndpoints()
    .WithReference(backend);

builder.AddProject<CustomerWebUI>("customerwebui")
    .WithReference(backend);

builder.Build().Run();
