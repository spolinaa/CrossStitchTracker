var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ReactApp1_Server>("reactapp1-server");

builder.Build().Run();
