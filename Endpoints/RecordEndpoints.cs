using System.Xml;
using DnsWeaverApi.BackgroundTasks;
using DnsWeaverApi.Configuration;
using DnsWeaverApi.Dns;
using DnsWeaverApi.Models;
using DnsWeaverApi.Sophos;
using Microsoft.AspNetCore.Mvc;

namespace DnsWeaverApi.Endpoints;

public static class RecordEndpoints
{
    public static void MapRecordEndpoints(this WebApplication app)
    {
        // The {instance} segment is never read — it exists only so each dnsweaver
        // provider instance can be given a distinct URL (e.g. .../dns/create vs
        // .../sophos-docker1/create). dnsweaver's own backend-identity dedup for
        // webhook providers is Type+Endpoint only (no awareness of our API keys),
        // so multiple instances pointing at the same URL get treated as one
        // backend and silently deduped — see provider.ProviderIdentity in
        // dnsweaver's webhook provider. Distinct URLs per instance route around
        // that, without needing any change to the API-key-based dispatch here.
        var group = app.MapGroup("/{instance}");

        group.MapGet("/ping", () => Results.Ok());

        group.MapGet("/health", async (HttpContext ctx, DnsWmiScopeProvider scopeProvider, SophosClient sophos, BackgroundTaskQueue queue, ILogger<Marker> logger) =>
        {
            var route = (ApiKeyRoute)ctx.Items["Route"]!;
            var healthy = true;
            var checks = new Dictionary<string, string>
            {
                ["queueDepth"] = queue.ApproximateCount.ToString()
            };

            if (IsSophosMode(route))
            {
                try
                {
                    var rule = await sophos.GetFirewallRuleAsync(route.SophosRuleName!);
                    checks["sophos"] = rule is not null ? "ok" : "rule-not-found";
                    healthy &= rule is not null;
                }
                catch (Exception ex) when (ex is HttpRequestException or SophosOperationException or XmlException)
                {
                    logger.LogWarning(ex, "Health check: Sophos unreachable");
                    checks["sophos"] = "unreachable";
                    healthy = false;
                }
            }
            else
            {
                try
                {
                    var scope = scopeProvider.GetScope();
                    checks["wmi"] = scope.IsConnected ? "ok" : "disconnected";
                    healthy &= scope.IsConnected;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Health check: WMI unreachable");
                    checks["wmi"] = "unreachable";
                    healthy = false;
                }
            }

            return healthy
                ? Results.Ok(checks)
                : Results.Json(checks, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        group.MapGet("/list", async (HttpContext ctx, DnsRecordService dns, SophosClient sophos, SophosDomainStateStore sophosState, ILogger<Marker> logger) =>
        {
            var route = (ApiKeyRoute)ctx.Items["Route"]!;
            try
            {
                if (IsSophosMode(route))
                {
                    var summary = await sophos.GetRuleSummaryAsync(route.SophosRuleName!);
                    return Results.Json(summary.Domains.Select(d => new
                    {
                        hostname = d,
                        type = "A",
                        // 1) exact value dnsweaver last sent us for this domain (best case);
                        // 2) the rule's backend IP, resolved live from Sophos (covers a cold
                        //    in-memory cache after an app pool recycle — only correct because
                        //    this deployment always sends that same backend IP as the target);
                        // 3) the rule name, as a last-resort non-null placeholder.
                        value = sophosState.TryGetLastValue(route.SophosRuleName!, d) ?? summary.BackendIp ?? route.SophosRuleName,
                        ttl = 300
                    }));
                }

                return Results.Json(dns.List());
            }
            catch (Exception ex) when (ex is WmiOperationException or SophosOperationException or HttpRequestException or XmlException)
            {
                return Problem(logger, ex, "list");
            }
        });

        group.MapPost("/create", (RecordRequest body, HttpContext ctx, HostnameValidator validator, DnsRecordService dns, SophosClient sophos, SophosDomainStateStore sophosState, BackgroundTaskQueue queue, ILogger<Marker> logger) =>
        {
            if (!validator.IsValid(body.Hostname))
                return Results.BadRequest(new { error = "invalid or out-of-zone hostname" });

            var route = (ApiKeyRoute)ctx.Items["Route"]!;

            if (IsSophosMode(route))
            {
                var ruleName = route.SophosRuleName!;
                var groupName = route.SophosGroupName;
                var hostname = body.Hostname;
                var value = body.Value;
                // Sophos WAF rule commits routinely take 30-90s to apply — longer
                // than the API manager's client timeout. Do the real work after
                // the response is already sent; dnsweaver's minute-scale reconcile
                // loop will see it land on the next /list either way.
                var accepted = queue.TryEnqueue($"create {SanitizeForLog(hostname)} on {ruleName}", async _ =>
                {
                    await sophos.AddDomainAsync(ruleName, hostname, groupName);
                    sophosState.Remember(ruleName, hostname, value);
                });
                if (!accepted)
                    return QueueSaturated(logger, "create", ruleName, hostname);
                return Results.StatusCode(201);
            }

            try
            {
                if (!DnsRecordService.IsSupportedType(body.Type))
                    return Results.BadRequest(new { error = $"unsupported type {body.Type}" });

                dns.Create(body);
                return Results.StatusCode(201);
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (WmiOperationException ex) { return Problem(logger, ex, "create"); }
        });

        group.MapPut("/update", (UpdateRequest body, HttpContext ctx, HostnameValidator validator, DnsRecordService dns, SophosClient sophos, SophosDomainStateStore sophosState, BackgroundTaskQueue queue, ILogger<Marker> logger) =>
        {
            if (!validator.IsValid(body.Hostname))
                return Results.BadRequest(new { error = "invalid or out-of-zone hostname" });

            var route = (ApiKeyRoute)ctx.Items["Route"]!;

            if (IsSophosMode(route))
            {
                var ruleName = route.SophosRuleName!;
                var groupName = route.SophosGroupName;
                var hostname = body.Hostname;
                var newValue = body.NewValue;
                // No meaningful "update" for list membership — adding is idempotent.
                var accepted = queue.TryEnqueue($"update {SanitizeForLog(hostname)} on {ruleName}", async _ =>
                {
                    await sophos.AddDomainAsync(ruleName, hostname, groupName);
                    sophosState.Remember(ruleName, hostname, newValue);
                });
                if (!accepted)
                    return QueueSaturated(logger, "update", ruleName, hostname);
                return Results.Ok();
            }

            try
            {
                if (!DnsRecordService.IsSupportedType(body.Type))
                    return Results.BadRequest(new { error = $"unsupported type {body.Type}" });

                dns.Update(body);
                return Results.Ok();
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (WmiOperationException ex) { return Problem(logger, ex, "update"); }
        });

        group.MapDelete("/delete", ([FromBody] DeleteRequest body, HttpContext ctx, HostnameValidator validator, DnsRecordService dns, SophosClient sophos, SophosDomainStateStore sophosState, BackgroundTaskQueue queue, ILogger<Marker> logger) =>
        {
            if (!validator.IsValid(body.Hostname))
                return Results.BadRequest(new { error = "invalid or out-of-zone hostname" });

            var route = (ApiKeyRoute)ctx.Items["Route"]!;

            if (IsSophosMode(route))
            {
                var ruleName = route.SophosRuleName!;
                var groupName = route.SophosGroupName;
                var hostname = body.Hostname;
                var accepted = queue.TryEnqueue($"delete {SanitizeForLog(hostname)} on {ruleName}", async _ =>
                {
                    await sophos.RemoveDomainAsync(ruleName, hostname, groupName);
                    sophosState.Forget(ruleName, hostname);
                });
                if (!accepted)
                    return QueueSaturated(logger, "delete", ruleName, hostname);
                return Results.Ok();
            }

            try
            {
                if (body.Type is null || !DnsRecordService.IsSupportedType(body.Type))
                    return Results.BadRequest(new { error = "type is required for delete" });

                dns.Delete(body);
                return Results.Ok(); // idempotent, dnsweaver accepte 200/204/404
            }
            catch (WmiOperationException ex) { return Problem(logger, ex, "delete"); }
        });
    }

    private static bool IsSophosMode(ApiKeyRoute route) =>
        string.Equals(route.Mode, "Sophos", StringComparison.OrdinalIgnoreCase);

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });

    // Hostname is validated to [A-Za-z0-9_.-] before any of these call sites run
    // (HostnameValidator), so CR/LF can't actually reach here today — but that's
    // a boolean gate elsewhere in the code, not a transformation static analysis
    // can see as a sanitizer at this sink. Strip newlines explicitly right before
    // logging so a log line can't be forged (CodeQL cs/log-forging) even if the
    // validation rule ever changes, and so the fix is visible at the point that
    // actually matters.
    private static string SanitizeForLog(string value) => value.Replace('\r', '_').Replace('\n', '_');

    // Full exception detail (WMI error codes, Sophos's raw error text, internal
    // hostnames/ports from HttpRequestException) goes to the server log only —
    // callers, even ones holding a valid but narrowly-scoped API key, get a
    // generic message rather than internal implementation details.
    private static IResult Problem(ILogger logger, Exception ex, string operation)
    {
        logger.LogError(ex, "{Operation} operation failed", operation);
        return Results.Problem("Une erreur interne est survenue. Consultez les logs serveur pour le détail.");
    }

    private static IResult QueueSaturated(ILogger logger, string operation, string ruleName, string hostname)
    {
        logger.LogError(
            "Background task queue full — dropping Sophos {Operation} for {Hostname} on rule {RuleName}",
            operation, SanitizeForLog(hostname), ruleName);
        return Results.Problem(
            "Le service est temporairement surchargé, réessayez plus tard.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // Marker type purely so ILogger<T> resolves a category name for this
    // static class — DI can't infer T from a class with no instances.
    private sealed class Marker;
}
