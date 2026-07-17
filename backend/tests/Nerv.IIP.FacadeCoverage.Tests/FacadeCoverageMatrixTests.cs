using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Nerv.IIP.FacadeCoverage.Tests;

/// <summary>
/// MAN-475 / #841 facade-coverage governance gate.
///
/// Reflects the live <c>*EndpointContracts.All</c> registry of every business service and
/// cross-checks it against the committed <c>facade-coverage-matrix.json</c> registry and the
/// two Gateway OpenAPI snapshots. A newly added service endpoint that is not classified in the
/// matrix fails the build — the structural gap X1/#784 recovered by full audit.
/// </summary>
public sealed class FacadeCoverageMatrixTests
{
    // Business service Web assemblies whose endpoint registries are governed. Adding a new
    // business service REQUIRES adding it here and as a ProjectReference in the csproj.
    private static readonly string[] BusinessWebAssemblyNames =
    [
        "Nerv.IIP.Business.Approval.Web",
        "Nerv.IIP.Business.BarcodeLabel.Web",
        "Nerv.IIP.Business.DemandPlanning.Web",
        "Nerv.IIP.Business.Erp.Web",
        "Nerv.IIP.Business.IndustrialTelemetry.Web",
        "Nerv.IIP.Business.Inventory.Web",
        "Nerv.IIP.Business.Maintenance.Web",
        "Nerv.IIP.Business.MasterData.Web",
        "Nerv.IIP.Business.Mes.Web",
        "Nerv.IIP.Business.ProductEngineering.Web",
        "Nerv.IIP.Business.Quality.Web",
        "Nerv.IIP.Business.Scheduling.Web",
        "Nerv.IIP.Business.Wms.Web",
    ];

    private static readonly Lazy<IReadOnlyDictionary<EndpointKey, string>> LiveEndpoints =
        new(DiscoverLiveEndpoints);

    private static readonly Lazy<IReadOnlyList<MatrixEntry>> Matrix = new(LoadMatrix);

    private static readonly Lazy<GatewaySnapshot> BusinessSnapshot =
        new(() => LoadSnapshot("business-gateway-console.v1.json"));

    private static readonly Lazy<GatewaySnapshot> PlatformSnapshot =
        new(() => LoadSnapshot("platform-gateway.v1.json"));

    private readonly record struct EndpointKey(string Service, string Method, string Route);

    [Fact]
    public void Live_registries_are_discoverable()
    {
        var live = LiveEndpoints.Value;
        // Guard against a silent reflection failure (e.g. a registry rename) masking coverage gaps.
        Assert.True(
            live.Count >= 300,
            $"Expected the reflected *EndpointContracts.All registries to yield >= 300 endpoints, " +
            $"but only found {live.Count}. A registry may have been renamed or an assembly failed to load.");
    }

    [Fact]
    public void New_service_endpoints_must_be_registered_in_the_matrix()
    {
        var live = LiveEndpoints.Value;
        var registered = MatrixKeys();

        var missing = live.Keys
            .Where(k => !registered.Contains(k))
            .OrderBy(k => k.Service, StringComparer.Ordinal)
            .ThenBy(k => k.Route, StringComparer.Ordinal)
            .ThenBy(k => k.Method, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These live business-service HTTP endpoints are not classified in " +
            "docs/architecture/facade-coverage-matrix.json. Every endpoint must declare a " +
            "consumption face (exposed / deferred / internal) — see facade-coverage-matrix.md:\n" +
            string.Join('\n', missing.Select(k => $"  {k.Service} {k.Method} {k.Route}")));
    }

    [Fact]
    public void Matrix_has_no_stale_rows()
    {
        var live = LiveEndpoints.Value;
        var stale = Matrix.Value
            .Select(ToKey)
            .Where(k => !live.ContainsKey(k))
            .OrderBy(k => k.Service, StringComparer.Ordinal)
            .ThenBy(k => k.Route, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            stale.Length == 0,
            "These facade-coverage-matrix.json rows no longer map to a live service endpoint " +
            "(route renamed or removed?). Remove or update them:\n" +
            string.Join('\n', stale.Select(k => $"  {k.Service} {k.Method} {k.Route}")));
    }

    [Fact]
    public void Matrix_operation_ids_match_live_registries()
    {
        var live = LiveEndpoints.Value;
        var mismatched = Matrix.Value
            .Where(e => live.TryGetValue(ToKey(e), out var op) && !string.Equals(op, e.OperationId, StringComparison.Ordinal))
            .Select(e => $"  {e.Service} {e.Method} {e.Route}: matrix='{e.OperationId}' live='{live[ToKey(e)]}'")
            .ToArray();

        Assert.True(
            mismatched.Length == 0,
            "OperationId in the matrix drifted from the live endpoint contract:\n" + string.Join('\n', mismatched));
    }

    [Fact]
    public void Classifications_are_valid_with_required_companion_fields()
    {
        var problems = new List<string>();
        foreach (var e in Matrix.Value)
        {
            switch (e.Classification)
            {
                case "exposed":
                    if (e.Gateways is not { Count: > 0 })
                    {
                        problems.Add($"  {e.Service} {e.Method} {e.Route}: exposed requires non-empty 'gateways'.");
                    }

                    if (e.GatewayOperationIds is not { Count: > 0 })
                    {
                        problems.Add($"  {e.Service} {e.Method} {e.Route}: exposed requires non-empty " +
                            "'gatewayOperationIds' (verifiable facade evidence, not just a note).");
                    }

                    break;
                case "deferred":
                    if (string.IsNullOrWhiteSpace(e.FollowUp))
                    {
                        problems.Add($"  {e.Service} {e.Method} {e.Route}: deferred requires a 'followUp' note.");
                    }
                    break;
                case "internal":
                    if (string.IsNullOrWhiteSpace(e.Rationale))
                    {
                        problems.Add($"  {e.Service} {e.Method} {e.Route}: internal requires a 'rationale'.");
                    }
                    break;
                default:
                    problems.Add($"  {e.Service} {e.Method} {e.Route}: unknown classification '{e.Classification}' " +
                        "(expected exposed | deferred | internal).");
                    break;
            }
        }

        Assert.True(problems.Count == 0, "Invalid facade-coverage classifications:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void Every_exposed_row_has_a_facade_operation_id_in_the_named_snapshot()
    {
        var problems = new List<string>();
        foreach (var e in Matrix.Value.Where(x => x.Classification == "exposed"))
        {
            if (e.GatewayOperationIds is not { Count: > 0 })
            {
                problems.Add($"  {e.Service} {e.Method} {e.Route}: exposed row has no gatewayOperationIds — " +
                    "exposed must carry verifiable facade evidence, not just a note.");
                continue;
            }

            var gateways = e.Gateways ?? [];
            foreach (var opId in e.GatewayOperationIds)
            {
                var inBusiness = gateways.Contains("business") && BusinessSnapshot.Value.OperationIds.Contains(opId);
                var inPlatform = gateways.Contains("platform") && PlatformSnapshot.Value.OperationIds.Contains(opId);
                if (!inBusiness && !inPlatform)
                {
                    problems.Add($"  {e.Service} {e.Method} {e.Route}: gatewayOperationId '{opId}' " +
                        $"not found in the {string.Join('/', gateways)} Gateway OpenAPI snapshot (facade missing?).");
                }
            }
        }

        Assert.True(
            problems.Count == 0,
            "Every 'exposed' row must record facade operationId(s) present in the Gateway snapshot " +
            "(the #784 failure mode: endpoint claims exposed but no facade shipped):\n" + string.Join('\n', problems));
    }

    [Fact]
    public void Deferred_or_internal_endpoints_are_not_silently_exposed()
    {
        var businessRoutes = BusinessSnapshot.Value.RouteKeys;
        var problems = new List<string>();
        foreach (var e in Matrix.Value.Where(x => x.Classification is "deferred" or "internal"))
        {
            var leaked = FacadeRouteCandidates(e.Method, e.Route).FirstOrDefault(businessRoutes.Contains);
            if (leaked is not null)
            {
                problems.Add($"  {e.Service} {e.Method} {e.Route}: classified '{e.Classification}' but a 1:1 facade " +
                    $"'{leaked}' exists in the BusinessGateway snapshot — reclassify it as 'exposed'.");
            }
        }

        Assert.True(
            problems.Count == 0,
            "A deferred/internal endpoint was exposed through a facade without updating its classification:\n" +
            string.Join('\n', problems));
    }

    [Fact]
    public void Summary_table_in_markdown_matches_the_registry()
    {
        var expected = Matrix.Value
            .GroupBy(e => e.Service, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (Total: g.Count(),
                      Exposed: g.Count(e => e.Classification == "exposed"),
                      Deferred: g.Count(e => e.Classification == "deferred"),
                      Internal: g.Count(e => e.Classification == "internal")),
                StringComparer.Ordinal);

        var actual = ParseSummaryTable(ReadResource("facade-coverage-matrix.md"));

        var problems = new List<string>();
        foreach (var (service, counts) in expected)
        {
            if (!actual.TryGetValue(service, out var row))
            {
                problems.Add($"  {service}: missing from the markdown summary table.");
            }
            else if (row != (counts.Total, counts.Exposed, counts.Deferred, counts.Internal))
            {
                problems.Add($"  {service}: markdown says {row} but registry has " +
                    $"({counts.Total}, {counts.Exposed}, {counts.Deferred}, {counts.Internal}).");
            }
        }

        var expectedTotal = expected.Values.Aggregate(
            (Total: 0, Exposed: 0, Deferred: 0, Internal: 0),
            (sum, counts) =>
                (sum.Total + counts.Total,
                 sum.Exposed + counts.Exposed,
                 sum.Deferred + counts.Deferred,
                 sum.Internal + counts.Internal));
        if (!actual.TryGetValue("Total", out var totalRow))
        {
            problems.Add("  Total: missing from the markdown summary table.");
        }
        else if (totalRow != expectedTotal)
        {
            problems.Add($"  Total: markdown says {totalRow} but registry has {expectedTotal}.");
        }

        foreach (var service in actual.Keys.Where(s => s != "Total" && !expected.ContainsKey(s)))
        {
            problems.Add($"  {service}: present in markdown summary but not in the registry.");
        }

        Assert.True(
            problems.Count == 0,
            "facade-coverage-matrix.md summary table is out of date with the JSON registry " +
            "(regenerate the FACADE-COVERAGE-SUMMARY block):\n" + string.Join('\n', problems));
    }

    // ---- discovery / loading helpers ----

    private static IReadOnlyDictionary<EndpointKey, string> DiscoverLiveEndpoints()
    {
        var result = new Dictionary<EndpointKey, string>();
        foreach (var assemblyName in BusinessWebAssemblyNames)
        {
            var assembly = LoadAssembly(assemblyName);
            var service = ServiceName(assemblyName);
            foreach (var type in SafeGetTypes(assembly))
            {
                if (!type.IsClass || !type.Name.EndsWith("EndpointContracts", StringComparison.Ordinal))
                {
                    continue;
                }

                if (ReadStaticAll(type) is not { } all)
                {
                    continue;
                }

                foreach (var contract in all)
                {
                    if (contract is null)
                    {
                        continue;
                    }

                    var method = ReadStringProperty(contract, "HttpMethod");
                    var route = ReadStringProperty(contract, "Route");
                    var operationId = ReadStringProperty(contract, "OperationId");
                    if (method is null || route is null || operationId is null)
                    {
                        continue;
                    }

                    result.TryAdd(new EndpointKey(service, method, route), operationId);
                }
            }
        }

        return result;
    }

    private static Assembly LoadAssembly(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, name + ".dll"));
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
    }

    private static IEnumerable? ReadStaticAll(Type type)
    {
        var value = type.GetProperty("All", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? type.GetField("All", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return value as IEnumerable;
    }

    private static string? ReadStringProperty(object instance, string propertyName)
        => instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) as string;

    private static string ServiceName(string assemblyName)
    {
        const string prefix = "Nerv.IIP.Business.";
        const string suffix = ".Web";
        return assemblyName.Substring(prefix.Length, assemblyName.Length - prefix.Length - suffix.Length);
    }

    private static IReadOnlyList<MatrixEntry> LoadMatrix()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var file = JsonSerializer.Deserialize<MatrixFile>(ReadResource("facade-coverage-matrix.json"), options)
            ?? throw new InvalidOperationException("facade-coverage-matrix.json failed to deserialize.");
        return file.Endpoints;
    }

    private static GatewaySnapshot LoadSnapshot(string resourceName)
    {
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        var routeKeys = new HashSet<string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(ReadResource(resourceName));
        if (document.RootElement.TryGetProperty("paths", out var paths))
        {
            foreach (var path in paths.EnumerateObject())
            {
                foreach (var operation in path.Value.EnumerateObject())
                {
                    if (operation.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (operation.Value.TryGetProperty("operationId", out var operationId)
                        && operationId.ValueKind == JsonValueKind.String)
                    {
                        operationIds.Add(operationId.GetString()!);
                    }

                    routeKeys.Add(RouteKey(operation.Name, path.Name));
                }
            }
        }

        return new GatewaySnapshot(operationIds, routeKeys);
    }

    private static HashSet<EndpointKey> MatrixKeys()
        => Matrix.Value.Select(ToKey).ToHashSet();

    private static EndpointKey ToKey(MatrixEntry e) => new(e.Service, e.Method, e.Route);

    // BusinessGateway facade routes mirror the downstream service route with the business-plane
    // prefix swapped for the console prefix. Used only to detect a 1:1 facade for a deferred/internal row.
    private static IEnumerable<string> FacadeRouteCandidates(string method, string route)
    {
        string[] transforms =
        [
            route.Replace("/api/business/v1/", "/api/business-console/v1/", StringComparison.Ordinal),
            route.Replace("/api/inventory/v1/", "/api/business-console/v1/inventory/", StringComparison.Ordinal),
            route.Replace("/api/inventory/v1/", "/api/business-console/v1/", StringComparison.Ordinal),
        ];
        foreach (var transformed in transforms.Distinct())
        {
            yield return RouteKey(method, transformed);
        }
    }

    private static string RouteKey(string method, string route)
        => method.ToUpperInvariant() + " " + NormalizeRoute(route);

    private static string NormalizeRoute(string route)
    {
        var questionMark = route.IndexOf('?');
        var path = questionMark >= 0 ? route[..questionMark] : route;
        var builder = new StringBuilder(path.Length);
        var inParameter = false;
        foreach (var c in path)
        {
            if (c == '{')
            {
                inParameter = true;
                builder.Append("{}");
            }
            else if (c == '}')
            {
                inParameter = false;
            }
            else if (!inParameter)
            {
                builder.Append(c);
            }
        }

        var normalized = builder.ToString();
        return normalized.Length > 1 && normalized.EndsWith('/') ? normalized.TrimEnd('/') : normalized;
    }

    private static IReadOnlyDictionary<string, (int Total, int Exposed, int Deferred, int Internal)> ParseSummaryTable(string markdown)
    {
        var rows = new Dictionary<string, (int, int, int, int)>(StringComparer.Ordinal);
        var inSummary = false;
        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("<!-- FACADE-COVERAGE-SUMMARY:START", StringComparison.Ordinal))
            {
                inSummary = true;
                continue;
            }

            if (line.StartsWith("<!-- FACADE-COVERAGE-SUMMARY:END", StringComparison.Ordinal))
            {
                break;
            }

            if (!inSummary || !line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length != 5)
            {
                continue;
            }

            var service = cells[0].Trim('*').Trim();
            if (service == "Service" || service.StartsWith("---", StringComparison.Ordinal))
            {
                continue; // header or separator
            }

            if (int.TryParse(cells[1].Trim('*').Trim(), out var total)
                && int.TryParse(cells[2].Trim('*').Trim(), out var exposed)
                && int.TryParse(cells[3].Trim('*').Trim(), out var deferred)
                && int.TryParse(cells[4].Trim('*').Trim(), out var @internal))
            {
                rows[service] = (total, exposed, deferred, @internal);
            }
        }

        return rows;
    }

    private static string ReadResource(string logicalName)
    {
        var assembly = typeof(FacadeCoverageMatrixTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{logicalName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record GatewaySnapshot(HashSet<string> OperationIds, HashSet<string> RouteKeys);

    private sealed class MatrixFile
    {
        public List<MatrixEntry> Endpoints { get; init; } = [];
    }

    private sealed class MatrixEntry
    {
        public string Service { get; init; } = string.Empty;
        public string Method { get; init; } = string.Empty;
        public string Route { get; init; } = string.Empty;
        public string OperationId { get; init; } = string.Empty;
        public string Classification { get; init; } = string.Empty;
        public List<string>? Gateways { get; init; }
        public List<string>? GatewayOperationIds { get; init; }
        public string? FollowUp { get; init; }
        public string? Rationale { get; init; }
        public string? Note { get; init; }
    }
}
