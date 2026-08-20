# DnsWeaverApi

ASP.NET Core webhook API that acts as a bridge between dnsweaver, a Windows DNS server, and Sophos WAF rules.

## Purpose

The service exposes a single HTTP contract for dnsweaver, then routes each request based on the provided API key:

- `Dns` mode: create, update, delete, and list Windows DNS records through WMI
- `Sophos` mode: add and remove domains from a Sophos Firewall WAF rule

This keeps a single webhook backend on the dnsweaver side while delegating the actual action to the correct target.

## Features

- API key authentication with per-instance routing
- `list`, `create`, `update`, and `delete` operations across multiple DNS record types
- Sophos rule support with a background queue to absorb slow operations
- strict hostname validation within the allowed zone
- OpenAPI specification maintained in [swagger.json](swagger.json)

## Requirements

- Windows, because DNS mode relies on WMI `root\MicrosoftDNS`
- a .NET SDK compatible with `net10.0-windows`
- access to the target Windows DNS server
- access to the Sophos API if `Sophos` mode is enabled

## Configuration

The project loads standard ASP.NET Core configuration, then an optional untracked `appsettings.Secrets.json` file.

1. Use [appsettings.json.example](appsettings.json.example) as the starting point.
2. Use `appsettings.Secrets.json` for real API keys and sensitive Sophos credentials. A sample is available in [appsettings.secrets.json.example](appsettings.secrets.json.example).
3. Fill in the `Dns` section for the Windows server and managed zone.
4. Fill in the `Sophos` section if you use Sophos mode.
5. Declare keys in `ApiKeys` with their associated routing.

Minimal example:

```json
{
  "Dns": {
    "Zone": "example.com",
    "ServerName": "dns-host"
  },
  "Auth": {
    "HeaderName": "X-API-Key"
  },
  "ApiKeys": {
    "my-key": {
      "Mode": "Dns"
    }
  }
}
```

## Local run

```powershell
dotnet restore
dotnet build
dotnet run
```

Local launch profiles are defined in [Properties/launchSettings.json](Properties/launchSettings.json).

## Releases

Pushing a Git tag matching `v*` triggers the GitHub Actions workflow in [.github/workflows/release-on-tag.yml](.github/workflows/release-on-tag.yml). It builds the project in `Release`, runs `dotnet publish`, creates a zip from the publish output, uploads it as a workflow artifact, and attaches the same zip to a GitHub Release.

## Endpoints

All endpoints are exposed under `/{instance}`:

- `GET /{instance}/ping`
- `GET /{instance}/list`
- `POST /{instance}/create`
- `PUT /{instance}/update`
- `DELETE /{instance}/delete`

The `instance` segment exists only to give each dnsweaver provider a distinct URL. Real routing depends on the API key, not on that path value.

## Project structure

- [Program.cs](Program.cs): application bootstrap
- [Endpoints/](Endpoints): HTTP contract and operation orchestration
- [Dns/](Dns): Windows DNS operations through WMI
- [Sophos/](Sophos): Sophos API integration
- [Security/](Security): API key authentication middleware
- [BackgroundTasks/](BackgroundTasks): queue and worker for asynchronous operations
- [Configuration/](Configuration) and [Models/](Models): options and DTOs

## API contract

The reference OpenAPI specification lives in [swagger.json](swagger.json). It documents schemas, response codes, and the mode-specific behavior of `Dns` and `Sophos`.

For concrete request bodies and sample responses, see [docs/payload-examples.md](docs/payload-examples.md).

That document also includes a dnsweaver environment-variable configuration example for both `Dns` and `Sophos` webhook providers.