using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsKnownExceptionMessageArchitectureTests
{
    private const string WmsDomainRoot =
        "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain";

    private const string WmsWebRoot =
        "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web";

    private static readonly IReadOnlyCollection<ExcludedUserMessageSite> ExcludedSites =
    [
        new($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "RecordWarehouseTaskProgressCommandHandler", "Handle", "内部手工执行回调，不是用户可见同步 transport"),
        new($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CompleteWarehouseTaskCommandHandler", "Handle", "内部手工执行回调，不是用户可见同步 transport"),
        new($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CloseBackorderOrderCommandHandler", "Handle", "延迟 backorder 处理，无同步公开 facade"),
        new($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "CancelOutboundOrderCommandHandler", "Handle", "取消出库为延迟/内部链路，无同步公开 facade"),
        new($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "MarkInventoryMovementRequestFailedCommandHandler", "Handle", "后台库存回调，不是用户可见同步 transport"),
        new($"{WmsWebRoot}/Application/Commands/WmsCommands.cs", "ResetWcsDispatchCircuitCommandHandler", "Handle", "内部 circuit 管理，无同步公开 facade"),
    ];

    [Fact]
    public void Wms_transport_visible_known_exceptions_use_safe_user_messages()
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
            .Select(file => new UserMessageSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();

        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(documents, ExcludedSites);

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
        var englishViolations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument("Probe.cs", englishSource)],
            []);
        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], englishViolations);

        const string dynamicSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(string message) { throw new KnownException(message); } }";
        var dynamicViolations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument("Probe.cs", dynamicSource)],
            []);
        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], dynamicViolations);

        var excluded = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument(
                "Internal.cs",
                "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal\"); } }")],
            [new ExcludedUserMessageSite("Internal.cs", "Probe", "Handle", "internal/no-facade")]);

        Assert.Empty(excluded);
    }

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
