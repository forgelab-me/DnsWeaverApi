using System.Text.Json;
using DnsWeaverApi.Endpoints;
using DnsWeaverApi.Extensions;
using DnsWeaverApi.Security;

var builder = WebApplication.CreateBuilder(args);

// Secrets (Sophos credentials, API keys) live outside source control in this
// optional file — deployed directly on the server, never published from git.
builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddDnsWeaverServices(builder.Configuration);

var app = builder.Build();

app.UseApiKeyAuth();
app.MapRecordEndpoints();

app.Run();
