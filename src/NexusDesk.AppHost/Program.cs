var builder = DistributedApplication.CreateBuilder(args);

// NexusDesk AppHost Orchestration configured with verified conflict-free port allocations
var api = builder.AddProject<Projects.NexusDesk_Api>("nexusdesk-api")
    .WithHttpEndpoint(port: 56100, name: "http");

builder.Build().Run();
