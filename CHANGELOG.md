# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.1.0] - 2026-08-27

### Added

- `GET /{instance}/health`: reports whether the backend a key actually routes to is reachable — WMI connectivity in `Dns` mode, or the targeted rule's existence in `Sophos` mode — plus the current background queue depth. Returns `503` when degraded.
- Automated test suite (`DnsWeaverApi.Tests`, xUnit) covering `HostnameValidator`, `SecureCompare`, `SophosDomainStateStore`, `BackgroundTaskQueue`, and `SophosClient` (against a fake HTTP handler, including a regression test for the Sophos rule-group membership bug — see Fixed in a previous internal iteration).
- CI workflow (`.github/workflows/ci.yml`): build and test run on every push and pull request to `main`.
- `BackgroundTaskQueue.ApproximateCount`, surfaced through `/health`, for visibility into pending background work.

### Changed

- WMI connections now reconnect automatically on failure (`DnsWmiScopeProvider`) instead of requiring an App Pool recycle after the DNS server restarts or a network blip drops the session.
- The background queue used for `Sophos` mode writes is now bounded (500 pending items) instead of unbounded. Once full, `create`, `update`, and `delete` return `503 Service Unavailable` instead of silently accepting work that would never run.
- Background Sophos operations now log a success line, not only failures — previously a completed queued write left no trace in the logs.

### Security

- Error responses from `list`, `create`, `update`, and `delete` no longer include internal exception details (WMI error codes, raw Sophos error text, HTTP/XML failure messages). Full detail is now logged server-side only; callers receive a generic message.
- The background task queue being unbounded was a latent resource-exhaustion risk under a sustained Sophos outage; it is now bounded with explicit rejection (see Changed).

## [1.0.0] - 2026-08-20

First public release.