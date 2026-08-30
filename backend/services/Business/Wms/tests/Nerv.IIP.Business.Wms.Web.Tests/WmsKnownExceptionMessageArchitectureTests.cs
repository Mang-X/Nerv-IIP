namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsKnownExceptionMessageArchitectureTests
{
    private const string WmsDomainRoot =
        "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain";

    private const string WmsWebRoot =
        "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web";

    private static readonly IReadOnlyCollection<string> SourcePaths =
    [
        $"{WmsDomainRoot}/AggregatesModel/OutboundOrderAggregate/OutboundOrder.cs",
        $"{WmsWebRoot}/Application/Commands/WmsCommands.cs",
        $"{WmsWebRoot}/Application/Inventory/WmsInventoryReservationClient.cs",
        $"{WmsWebRoot}/Application/Queries/ListQueryCriteria.cs",
    ];

    private static readonly IReadOnlyCollection<WmsKnownExceptionSite> ExpectedSites =
    [
        Target($"{WmsDomainRoot}/AggregatesModel/OutboundOrderAggregate/OutboundOrder.cs", "OutboundOrder", "EnsurePickingQuantity", 2),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CreateInboundOrderCommandHandler", "Handle", 1),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CreatePutawayTaskCommandHandler", "Handle", 2),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CompleteInboundOrderCommandHandler", "Handle", 1),
        Excluded($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "RetryInboundInventoryPostingCommandHandler", "Handle", 1, "异步/延迟库存过账，无同步公开 facade"),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CreatePickingTaskCommandHandler", "Handle", 3),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CreatePickingTaskCommandHandler", "ReserveInventoryForPickingAsync", 2),
        Excluded($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "RecordWarehouseTaskProgressCommandHandler", "Handle", 1, "内部手工执行回调，不是用户可见同步 transport"),
        Excluded($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CompleteWarehouseTaskCommandHandler", "Handle", 1, "内部手工执行回调，不是用户可见同步 transport"),
        Excluded($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CloseBackorderOrderCommandHandler", "Handle", 2, "延迟 backorder 处理，无同步公开 facade"),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CompleteOutboundOrderCommandHandler", "Handle", 2),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CompleteOutboundOrderCommandHandler", "EnsureInventoryClientAvailableForShortPickRelease", 1),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CompleteOutboundOrderCommandHandler", "ReleaseShortPickedReservationBalancesAsync", 1),
        Excluded($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CancelOutboundOrderCommandHandler", "Handle", 2, "取消出库为延迟/内部链路，无同步公开 facade"),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "RetryOutboundInventoryPostingCommandHandler", "Handle", 2),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CompleteCountExecutionCommandHandler", "Handle", 3),
        Excluded($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "MarkInventoryMovementRequestFailedCommandHandler", "Handle", 1, "后台库存回调，不是用户可见同步 transport"),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CompleteWcsTaskCommandHandler", "Handle", 2),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "WcsTaskCallbackCommandLock", "GetLockKeysAsync", 1),
        Target($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "FailWcsTaskCommandHandler", "Handle", 2),
        Excluded($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "ResetWcsDispatchCircuitCommandHandler", "Handle", 1, "内部 circuit 管理，无同步公开 facade"),
        Target($"{WmsWebRoot}/Application/Inventory/WmsInventoryReservationClient.cs", "HttpWmsInventoryReservationClient", "ReserveAsync", 1),
        Target($"{WmsWebRoot}/Application/Inventory/WmsInventoryReservationClient.cs", "HttpWmsInventoryReservationClient", "ReserveFefoAsync", 1),
        Target($"{WmsWebRoot}/Application/Inventory/WmsInventoryReservationClient.cs", "HttpWmsInventoryReservationClient", "ReleaseAsync", 1),
        Target($"{WmsWebRoot}/Application/Inventory/WmsInventoryReservationClient.cs", "HttpWmsInventoryReservationClient", "RenewAsync", 1),
        Target($"{WmsWebRoot}/Application/Inventory/WmsInventoryReservationClient.cs", "HttpWmsInventoryReservationClient", "CreateCountTaskAsync", 1),
        Target($"{WmsWebRoot}/Application/Inventory/WmsInventoryReservationClient.cs", "HttpWmsInventoryReservationClient", "ConfirmCountAdjustmentAsync", 1),
        Target($"{WmsWebRoot}/Application/Queries/ListQueryCriteria.cs", "TenantScope", "From", 2),
    ];

    [Fact]
    public void Wms_transport_visible_known_exceptions_have_a_closed_target_and_exclusion_ledger()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot, WmsDomainRoot.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(repositoryRoot, WmsWebRoot.Replace('/', Path.DirectorySeparatorChar)),
        };
        var sourceFiles = sourceRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        var documents = sourceFiles
            .Select(file => new WmsSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();

        var documentPaths = documents.Select(document => document.Path).ToHashSet(StringComparer.Ordinal);
        Assert.All(SourcePaths, path => Assert.Contains(path, documentPaths));
        Assert.All(documents, document => Assert.False(string.IsNullOrEmpty(document.Text), $"Wms 源文件缺失或为空：{document.Path}"));

        var expectedKeys = ExpectedSites.Select(site => site.Key).ToArray();
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(33, ExpectedSites.Where(site => site.Kind == WmsKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(9, ExpectedSites.Where(site => site.Kind == WmsKnownExceptionSiteKind.Excluded)
            .Sum(site => site.DirectKnownExceptionCount));

        var discovered = WmsUserMessageSourceAnalyzer.Discover(documents);
        var duplicateDiscoveredKeys = discovered
            .GroupBy(site => site.Key, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => $"{group.Key} ({group.Count()})")
            .ToArray();
        Assert.Empty(duplicateDiscoveredKeys);

        var expectedKeySet = expectedKeys.ToHashSet(StringComparer.Ordinal);
        var unclassified = discovered
            .Where(site => !expectedKeySet.Contains(site.Key))
            .Select(site => site.Key)
            .ToArray();
        Assert.True(
            unclassified.Length == 0,
            "Wms KnownException 投点未被 target/exclusion ledger 分类："
            + Environment.NewLine
            + string.Join(Environment.NewLine, unclassified));
        Assert.Equal(expectedKeySet.Count, discovered.Count);

        foreach (var expected in ExpectedSites)
        {
            var matches = discovered.Where(site => site.Key == expected.Key).ToArray();
            Assert.Single(matches);
            Assert.Equal(expected.DirectKnownExceptionCount, matches[0].DirectKnownExceptionCount);
        }

        var violations = WmsUserMessageSourceAnalyzer.Analyze(
            documents,
            ExpectedSites
                .Where(site => site.Kind == WmsKnownExceptionSiteKind.Excluded)
                .Select(site => new WmsExcludedSite(site.Path, site.TypeName, site.MethodName, site.Reason))
                .ToArray());

        Assert.True(
            violations.Count == 0,
            "Wms transportVisible 用户消息必须是中文、静态可分析、长度不超过 60 且不含危险字符。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Analyzer_reports_dynamic_and_unsafe_mutations_and_honors_exclusion()
    {
        const string englishSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"Inventory failed.\"); } }";
        var englishViolations = WmsUserMessageSourceAnalyzer.Analyze(
            [new WmsSourceDocument("Probe.cs", englishSource)],
            []);
        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], englishViolations);

        const string dynamicSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(string message) { throw new KnownException(message); } }";
        var dynamicViolations = WmsUserMessageSourceAnalyzer.Analyze(
            [new WmsSourceDocument("Probe.cs", dynamicSource)],
            []);
        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], dynamicViolations);

        var excluded = WmsUserMessageSourceAnalyzer.Analyze(
            [new WmsSourceDocument(
                "Internal.cs",
                "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal\"); } }")],
            [new WmsExcludedSite("Internal.cs", "Probe", "Handle", "internal/no-facade")]);

        Assert.Empty(excluded);
    }

    private static WmsKnownExceptionSite Target(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount) =>
        new(path, typeName, methodName, directKnownExceptionCount, WmsKnownExceptionSiteKind.Target, "同步公开 Wms transportVisible 路径");

    private static WmsKnownExceptionSite Excluded(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        string reason) =>
        new(path, typeName, methodName, directKnownExceptionCount, WmsKnownExceptionSiteKind.Excluded, reason);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
