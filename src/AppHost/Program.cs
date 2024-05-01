using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var backendDb = builder
    .AddPostgres("eshopsupport-postgres").WithPgAdmin()
    .AddDatabase("backenddb");

var backend = builder.AddProject<Backend>("backend")
    .WithReference(backendDb);

builder.AddProject<StaffWebUI>("staffwebui")
    .WithReference(backend);

builder.Build().Run();
