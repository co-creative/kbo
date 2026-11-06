var builder = DistributedApplication.CreateBuilder(args);

// Locally the web app serves Kbo:Database from its appsettings.Development.json (../data/kbo-<extract>.db).
// Azure resources (file share, build job) are added in phase 3 (specs/azure-architecture.md §8).
builder.AddProject<Projects.Net_Code_Kbo_Web>("kbo-web");

builder.Build().Run();
