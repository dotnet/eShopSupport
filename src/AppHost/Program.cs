using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var backend = builder.AddProject<Backend>("backend");

builder.AddProject<StaffWebUI>("staffwebui")
    .WithReference(backend);

builder.Build().Run();
