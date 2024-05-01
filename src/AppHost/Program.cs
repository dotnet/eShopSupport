using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var dbPassword = builder.AddParameter("PostgresPassword", secret: true);

var backendDb = builder
    .AddPostgres("eshopsupport-postgres", password: dbPassword)
    .WithDataVolume()
    .AddDatabase("backenddb");

var backend = builder.AddProject<Backend>("backend")
    .WithReference(backendDb);

builder.AddProject<StaffWebUI>("staffwebui")
    .WithExternalHttpEndpoints()
    .WithReference(backend);

builder.Build().Run();
