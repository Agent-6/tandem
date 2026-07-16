var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Authentication_API>("authentication-api");

builder.Build().Run();
