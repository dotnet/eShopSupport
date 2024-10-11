using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);
builder.Configuration.Sources.Add(new JsonConfigurationSource { Path = "appsettings.Local.json", Optional = true });

var isE2ETest = builder.Configuration["E2E_TEST"] == "true";

var dbPassword = builder.AddParameter("PostgresPassword", secret: true);

var postgresServer = builder
    .AddPostgres("eshopsupport-postgres", password: dbPassword);
var backendDb = postgresServer
    .AddDatabase("backenddb");

var vectorDb = builder
    .AddQdrant("vector-db");

var identityServer = builder.AddProject<IdentityServer>("identity-server")
    .WithExternalHttpEndpoints();

var identityEndpoint = identityServer
    .GetEndpoint("https");

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

var stage = builder.ExecutionContext.IsRunMode ? "base" : /* default stage */ null;
var pythonInference = builder.AddDockerfile("python-inference", "../PythonInference", /* default dockerfile */ null, stage)
    .WithHttpEndpoint(targetPort: 62394, env: "UVICORN_PORT")
    .WithContainerRuntimeArgs("--gpus=all")
    .WithLifetime(ContainerLifetime.Persistent);

if (builder.ExecutionContext.IsRunMode)
{
    // Mount app files into the container & enable auto-reload when running in development
    pythonInference
        .WithBindMount("../PythonInference", "/app")
        .WithArgs("--reload");
}

var redis = builder.AddRedis("redis");

var backend = builder.AddProject<Backend>("backend")
    .WithReference(backendDb)
    .WithReference(chatCompletion)
    .WithReference(blobStorage)
    .WithReference(vectorDb)
    .WithReference(pythonInference.GetEndpoint("http"))
    .WithReference(redis)
    .WithEnvironment("IdentityUrl", identityEndpoint)
    .WithEnvironment("ImportInitialDataDir", Path.Combine(builder.AppHostDirectory, "..", "..", "seeddata", isE2ETest ? "test" : "dev"));

var staffWebUi = builder.AddProject<StaffWebUI>("staffwebui")
    .WithExternalHttpEndpoints()
    .WithReference(backend)
    .WithReference(redis)
    .WithEnvironment("IdentityUrl", identityEndpoint);

var customerWebUi = builder.AddProject<CustomerWebUI>("customerwebui")
    .WithReference(backend)
    .WithEnvironment("IdentityUrl", identityEndpoint);

// Circular references: IdentityServer needs to know the endpoints of the web UIs
identityServer
    .WithEnvironment("CustomerWebUIEndpoint", customerWebUi.GetEndpoint("https"))
    .WithEnvironment("StaffWebUIEndpoint", staffWebUi.GetEndpoint("https"));

if (!isE2ETest)
{
    postgresServer.WithDataVolume();
    vectorDb.WithVolume("eshopsupport-vector-db-storage", "/qdrant/storage");
}

builder.Build().Run();
