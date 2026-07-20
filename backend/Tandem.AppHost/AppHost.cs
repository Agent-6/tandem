using k8s.Models;

var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure resources
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var tandemDb = postgres.AddDatabase("tandemdb");

var redis = builder.AddRedis("redis")
    .WithRedisInsight();

// Backend API (ASP.NET Core + SignalR)
var api = builder.AddProject<Projects.Tandem_Api>("api")
    .WithReference(tandemDb)
    .WaitFor(tandemDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithExternalHttpEndpoints();

// Frontend (Angular dev server)
builder.AddJavaScriptApp("frontend", "../../frontend", "start")
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("API_HTTP", api.GetEndpoint("http"));

builder.Build().Run();
