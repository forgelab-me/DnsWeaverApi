# DnsWeaverApi

[![CI](https://github.com/forgelab-me/DnsWeaverApi/actions/workflows/ci.yml/badge.svg)](https://github.com/forgelab-me/DnsWeaverApi/actions/workflows/ci.yml)

ASP.NET Core webhook API that acts as a bridge between dnsweaver, a Windows DNS server, and Sophos WAF rules.

## Purpose

The service exposes a single HTTP contract for dnsweaver, then routes each request based on the provided API key:

- `Dns` mode: create, update, delete, and list Windows DNS records through WMI
- `Sophos` mode: add and remove domains from a Sophos Firewall WAF rule

This keeps a single webhook backend on the dnsweaver side while delegating the actual action to the correct target.

## Features

- API key authentication with per-instance routing
- `list`, `create`, `update`, and `delete` operations across multiple DNS record types
- Sophos rule support with a bounded background queue to absorb slow operations, without accepting work it can't eventually run
- automatic WMI reconnection if the DNS server restarts or a network blip drops the connection
- a `health` endpoint reporting live backend reachability (WMI or Sophos) and background queue depth
- strict hostname validation within the allowed zone
- OpenAPI specification maintained in [swagger.json](swagger.json)
- automated test suite and CI (see [Testing](#testing))

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

## Testing

```powershell
dotnet test
```

`DnsWeaverApi.Tests` (xUnit) covers `HostnameValidator`, `SecureCompare`, `SophosDomainStateStore`, `BackgroundTaskQueue`, and `SophosClient` — the last one against a fake `HttpMessageHandler` returning canned Sophos XML responses, so it runs without a real firewall.

`DnsRecordService` itself is not unit-tested beyond `IsSupportedType`: it talks to a real Windows DNS server over WMI (`root\MicrosoftDNS`), and `DnsWmiScopeProvider` connects on construction, so exercising it meaningfully needs an actual DNS server rather than a mock. It's verified manually against a real server instead.

CI (`.github/workflows/ci.yml`) runs `dotnet build` and `dotnet test` on every push and pull request to `main`.

## Releases

Pushing a Git tag matching `v*` triggers the GitHub Actions workflow in [.github/workflows/publish.yml](.github/workflows/publish.yml). It builds the project in `Release`, runs `dotnet publish`, creates a zip from the publish output, uploads it as a workflow artifact, and attaches the same zip to a GitHub Release.

## Endpoints

All endpoints are exposed under `/{instance}`:

- `GET /{instance}/ping`
- `GET /{instance}/health`
- `GET /{instance}/list`
- `POST /{instance}/create`
- `PUT /{instance}/update`
- `DELETE /{instance}/delete`

The `instance` segment exists only to give each dnsweaver provider a distinct URL. Real routing depends on the API key, not on that path value.

`health` reports the reachability of whatever backend the key actually routes to — WMI connectivity in `Dns` mode, or the targeted rule's existence in `Sophos` mode — plus the current background queue depth. It returns `503` when degraded.

## Project structure

- [Program.cs](Program.cs): application bootstrap
- [Endpoints/](Endpoints): HTTP contract and operation orchestration
- [Dns/](Dns): Windows DNS operations through WMI
- [Sophos/](Sophos): Sophos API integration
- [Security/](Security): API key authentication middleware
- [BackgroundTasks/](BackgroundTasks): queue and worker for asynchronous operations
- [Configuration/](Configuration) and [Models/](Models): options and DTOs
- [DnsWeaverApi.Tests/](DnsWeaverApi.Tests): xUnit test suite (see [Testing](#testing))

## API contract

The reference OpenAPI specification lives in [swagger.json](swagger.json). It documents schemas, response codes, and the mode-specific behavior of `Dns` and `Sophos`.

For concrete request bodies and sample responses, see [docs/payload-examples.md](docs/payload-examples.md).

That document also includes a dnsweaver environment-variable configuration example for both `Dns` and `Sophos` webhook providers.