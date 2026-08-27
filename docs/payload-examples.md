# API Payload Examples

This document shows example requests for the DnsWeaverApi webhook endpoints.

## Base pattern

All endpoints are exposed under `/{instance}` and require the API key header:

```http
X-API-Key: your-api-key
```

Example base URL:

```text
https://localhost:62011/dns
```

The `instance` path segment is only used to give each dnsweaver provider a distinct URL. The actual routing behavior depends on the API key.

## dnsweaver configuration example

The following environment variables show how to declare two webhook providers in dnsweaver:

- one provider targeting `Dns` mode at `/dns`
- one provider targeting `Sophos` mode at `/sophos-docker1`

```yaml
- DNSWEAVER_WEBHOOK_TYPE=webhook
- DNSWEAVER_WEBHOOK_URL=https://example.com/dns
- DNSWEAVER_WEBHOOK_AUTH_HEADER=X-API-Key
- DNSWEAVER_WEBHOOK_AUTH_TOKEN=118054c8-b759-462b-83f4-c22b33735298
- DNSWEAVER_WEBHOOK_RECORD_TYPE=A
- DNSWEAVER_WEBHOOK_TARGET=REPLACE_BY_PUBLIC_IP
- DNSWEAVER_WEBHOOK_DOMAINS=*.example.com
- DNSWEAVER_WEBHOOK_TLS_CA_FILE=/data/ROOT-CA.crt

- DNSWEAVER_SOPHOS_TYPE=webhook
- DNSWEAVER_SOPHOS_URL=https://example.com/sophos-docker1
- DNSWEAVER_SOPHOS_AUTH_HEADER=X-API-Key
- DNSWEAVER_SOPHOS_AUTH_TOKEN=docker-1-118054c8-b759-462b-83f4-c22b33735298
- DNSWEAVER_SOPHOS_RECORD_TYPE=A
- DNSWEAVER_SOPHOS_TARGET=REPLACE_BY_PRIVATE_IP
- DNSWEAVER_SOPHOS_DOMAINS=*.example.com
- DNSWEAVER_SOPHOS_TLS_CA_FILE=/data/ROOT-CA.crt
```

Matching server-side routing example in `appsettings.Secrets.json`:

```json
{
  "ApiKeys": {
    "118054c8-b759-462b-83f4-c22b33735298": {
      "Mode": "Dns"
    },
    "docker-1-118054c8-b759-462b-83f4-c22b33735298": {
      "Mode": "Sophos",
      "SophosRuleName": "WAN TO DOCKER-1",
      "SophosGroupName": "WAN to LAN"
    }
  }
}
```

Notes:

- use a distinct URL per dnsweaver provider instance, even when both point to the same API deployment
- the API key decides whether the request is routed to `Dns` mode or `Sophos` mode
- `DNSWEAVER_WEBHOOK_TARGET` and `DNSWEAVER_SOPHOS_TARGET` are values dnsweaver sends as the record target; in `Sophos` mode the same contract is accepted even though the backend action is domain-list management rather than a real DNS write
- if your API certificate is signed by a private CA, point `*_TLS_CA_FILE` to that CA certificate inside the dnsweaver container

## Ping

```http
GET /dns/ping
X-API-Key: your-api-key
```

## Health

```http
GET /dns/health
X-API-Key: your-api-key
```

Example response, `Dns` mode key:

```json
{
  "wmi": "ok",
  "queueDepth": "0"
}
```

Example response, `Sophos` mode key:

```json
{
  "sophos": "ok",
  "queueDepth": "0"
}
```

Returns `503` with the same shape (value `"disconnected"`, `"unreachable"`, or `"rule-not-found"` instead of `"ok"`) when the backend for that key isn't reachable.

## List records

```http
GET /dns/list
X-API-Key: your-api-key
```

Example response:

```json
[
  {
    "hostname": "app.example.com",
    "type": "A",
    "value": "10.0.0.42",
    "ttl": 300,
    "srv": null
  }
]
```

## Create record

### A record

```http
POST /dns/create
Content-Type: application/json
X-API-Key: your-api-key
```

```json
{
  "hostname": "app.example.com",
  "type": "A",
  "value": "10.0.0.42",
  "ttl": 300,
  "srv": null
}
```

### AAAA record

```json
{
  "hostname": "ipv6.example.com",
  "type": "AAAA",
  "value": "2001:db8::42",
  "ttl": 300,
  "srv": null
}
```

### CNAME record

```json
{
  "hostname": "www.example.com",
  "type": "CNAME",
  "value": "app.example.com",
  "ttl": 300,
  "srv": null
}
```

### TXT record

```json
{
  "hostname": "_acme-challenge.example.com",
  "type": "TXT",
  "value": "challenge-token",
  "ttl": 60,
  "srv": null
}
```

### SRV record

```json
{
  "hostname": "_minecraft._tcp.example.com",
  "type": "SRV",
  "value": "mc.example.com",
  "ttl": 300,
  "srv": {
    "priority": 10,
    "weight": 5,
    "port": 25565
  }
}
```

## Update record

`update` is the only payload that uses `old_value`, `new_value`, and `old_srv` in snake_case. This matches dnsweaver's webhook payload format.

### A record update

```http
PUT /dns/update
Content-Type: application/json
X-API-Key: your-api-key
```

```json
{
  "hostname": "app.example.com",
  "type": "A",
  "old_value": "10.0.0.41",
  "new_value": "10.0.0.42",
  "ttl": 300,
  "srv": null,
  "old_srv": null
}
```

### SRV record update

```json
{
  "hostname": "_minecraft._tcp.example.com",
  "type": "SRV",
  "old_value": "old-mc.example.com",
  "new_value": "mc.example.com",
  "ttl": 300,
  "srv": {
    "priority": 10,
    "weight": 5,
    "port": 25565
  },
  "old_srv": {
    "priority": 5,
    "weight": 5,
    "port": 25565
  }
}
```

## Delete record

### DNS mode

In `Dns` mode, `type` is required.

```http
DELETE /dns/delete
Content-Type: application/json
X-API-Key: your-api-key
```

```json
{
  "hostname": "app.example.com",
  "type": "A"
}
```

### Sophos mode

In `Sophos` mode, `type` is ignored and can be omitted.

```json
{
  "hostname": "app.example.com"
}
```

## cURL example

```bash
curl -X POST "https://localhost:62011/dns/create" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key" \
  -d '{
    "hostname": "app.example.com",
    "type": "A",
    "value": "10.0.0.42",
    "ttl": 300,
    "srv": null
  }'
```

## Notes

- The same HTTP contract is used for both `Dns` and `Sophos` modes.
- Hostnames must pass validation and remain inside the configured DNS zone.
- In `Sophos` mode, `create` and `update` are effectively idempotent domain-add operations.
- The full contract reference remains in [swagger.json](../swagger.json).