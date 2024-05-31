using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Projects;

var isE2ETest = Environment.GetEnvironmentVariable("E2E_TEST") == "true";

var builder = DistributedApplication.CreateBuilder(args);
builder.Configuration.Sources.Add(new JsonConfigurationSource { Path = "appsettings.Local.json", Optional = true });

var dbPassword = builder.AddParameter("PostgresPassword", secret: true);

var postgresServer = builder
    .AddPostgres("eshopsupport-postgres", password: dbPassword);
var backendDb = postgresServer
    .AddDatabase("backenddb");

var vectorDb = builder
    .AddContainer("vector-db", "qdrant/qdrant", "latest")
    .WithHttpEndpoint(port: 62392, targetPort: 6333);

// Use this if you want to use Ollama
var chatCompletion = builder.AddOllama("chatcompletion").WithDataVolume();

// ... or use this if you want to use OpenAI (having also configured the API key in appsettings)
//var chatCompletion = builder.AddConnectionString("chatcompletion");

var storage = builder.AddAzureStorage("eshopsupport-storage");
if (builder.Environment.IsDevelopment())
{
    storage.RunAsEmulator(r =>
    {
        if (!isE2ETest)
        {
            r.WithDataVolume();
        }
    });
}

var blobStorage = storage.AddBlobs("eshopsupport-blobs");

var pythonInference = builder.AddPythonUvicornApp("python-inference",
    Path.Combine("..", "PythonInference"), port: 62394);

var redis = builder.AddRedis("redis");

var backend = builder.AddProject<Backend>("backend")
    .WithReference(backendDb)
    .WithReference(chatCompletion)
    .WithReference(blobStorage)
    .WithReference(vectorDb.GetEndpoint("http"))
    .WithReference(pythonInference)
    .WithReference(redis)
    .WithEnvironment("ImportInitialDataDir", Path.Combine(builder.AppHostDirectory, "..", "..", "seeddata", isE2ETest ? "test" : "dev"));

builder.AddProject<StaffWebUI>("staffwebui")
    .WithExternalHttpEndpoints()
    .WithReference(backend)
    .WithReference(redis);

builder.AddProject<CustomerWebUI>("customerwebui")
    .WithReference(backend);

if (!isE2ETest)
{
    postgresServer.WithDataVolume();
    vectorDb.WithVolume("eshopsupport-vector-db-storage", "/qdrant/storage");
}

builder.Build().Run();
