namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelKnownExceptionMessageArchitectureTests
{
    private const string BarcodeLabelSourceRoot =
        "backend/services/Business/BarcodeLabel/src";

    private static readonly IReadOnlyCollection<string> SourcePaths =
    [
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Infrastructure/ApplicationDbContext.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/BarcodeRules/CreateOrUpdateBarcodeRuleCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/LabelTemplates/CreateOrUpdateLabelTemplateCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/CreateLabelPrintBatchCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/Scans/RecordScanCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/PrintBatches/GetLabelPrintBatchQuery.cs",
    ];

    private static readonly IReadOnlyCollection<BarcodeLabelKnownExceptionSite> ExpectedSites =
    [
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Infrastructure/ApplicationDbContext.cs"), "ApplicationDbContext", "TryMapUniqueConflict", 2),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/BarcodeRules/CreateOrUpdateBarcodeRuleCommand.cs"), "CreateOrUpdateBarcodeRuleCommandHandler", "Handle", 4),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/LabelTemplates/CreateOrUpdateLabelTemplateCommand.cs"), "CreateOrUpdateLabelTemplateCommandHandler", "Handle", 2),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/CreateLabelPrintBatchCommand.cs"), "CreateLabelPrintBatchCommandHandler", "Handle", 4),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "DispatchLabelPrintBatchCommandHandler", "Handle", 2),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "ReprintLabelCommandHandler", "Handle", 5),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "VoidLabelCommandHandler", "Handle", 3),
        Excluded(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "LoadBatchAsync", 1, "PrintLabel lifecycle helper is internal/no-facade"),
        Excluded(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "CompileFrozenBatchAsync", 1, "PrintLabel lifecycle helper is internal/no-facade"),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/Scans/RecordScanCommand.cs"), "RecordScanCommandHandler", "Handle", 6),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/PrintBatches/GetLabelPrintBatchQuery.cs"), "GetLabelPrintBatchQueryHandler", "Handle", 1),
    ];

    [Fact]
    public void BarcodeLabel_transport_visible_known_exceptions_have_a_closed_target_and_exclusion_ledger()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, BarcodeLabelSourceRoot.Replace('/', Path.DirectorySeparatorChar));
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        var documents = sourceFiles
            .Select(file => new BarcodeLabelSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();

        var documentPaths = documents.Select(document => document.Path).ToHashSet(StringComparer.Ordinal);
        Assert.All(SourcePaths, path => Assert.Contains(path, documentPaths));
        Assert.All(documents, document => Assert.False(string.IsNullOrEmpty(document.Text), $"BarcodeLabel 源文件缺失或为空：{document.Path}"));

        var expectedKeys = ExpectedSites.Select(site => site.Key).ToArray();
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, ExpectedSites.Count(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Excluded));
        Assert.DoesNotContain(
            ExpectedSites,
            site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Excluded
                && site.TypeName.EndsWith("Handler", StringComparison.Ordinal));
        Assert.Equal(29, ExpectedSites.Where(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(2, ExpectedSites.Where(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Excluded)
            .Sum(site => site.DirectKnownExceptionCount));

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(documents);
        var duplicateDiscoveredKeys = discovered
            .GroupBy(site => site.Key, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => $"{group.Key} ({group.Count()})")
            .ToArray();
        Assert.Empty(duplicateDiscoveredKeys);

        var ledgerViolations = BarcodeLabelUserMessageSourceAnalyzer.ValidateLedger(
            discovered,
            ExpectedSites,
            expectedExclusionCount: 2);
        Assert.True(
            ledgerViolations.Count == 0,
            "BarcodeLabel KnownException 位点台账未闭合："
            + Environment.NewLine
            + string.Join(Environment.NewLine, ledgerViolations));

        foreach (var expected in ExpectedSites)
        {
            var matches = discovered.Where(site => site.Key == expected.Key).ToArray();
            Assert.Single(matches);
            Assert.Equal(expected.DirectKnownExceptionCount, matches[0].DirectKnownExceptionCount);
        }

        var violations = BarcodeLabelUserMessageSourceAnalyzer.Analyze(documents);

        Assert.True(
            violations.Count == 0,
            "BarcodeLabel transportVisible 用户消息必须是中文、静态可分析、长度不超过 60 且不含危险字符。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Analyzer_reports_dynamic_unsafe_and_excluded_site_messages()
    {
        const string englishSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"Barcode failed.\"); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息必须包含中文。"],
            BarcodeLabelUserMessageSourceAnalyzer.Analyze(
                [new BarcodeLabelSourceDocument("Probe.cs", englishSource)]));

        const string dynamicSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(string message) { throw new KnownException(message); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"],
            BarcodeLabelUserMessageSourceAnalyzer.Analyze(
                [new BarcodeLabelSourceDocument("Probe.cs", dynamicSource)]));

        const string unsafeSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"无法保存 <内容>。\"); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息不能包含不安全字符。"],
            BarcodeLabelUserMessageSourceAnalyzer.Analyze(
                [new BarcodeLabelSourceDocument("Probe.cs", unsafeSource)]));

        var excluded = BarcodeLabelUserMessageSourceAnalyzer.Analyze(
            [new BarcodeLabelSourceDocument(
                "Internal.cs",
                "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal-code\"); } }")]);

        Assert.Equal(["Internal.cs:1: 用户消息必须包含中文。"], excluded);
    }

    [Fact]
    public void Discovery_includes_command_handlers_that_have_no_known_exception_site()
    {
        const string source = """
            interface ICommandHandler<TCommand, TResult> { }
            sealed class ProbeHandler : ICommandHandler<int, string>
            {
                public string Handle(int command) => command.ToString();
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Probe.cs", source)]);

        var handler = Assert.Single(discovered);
        Assert.Equal("ProbeHandler", handler.TypeName);
        Assert.Equal("Handle", handler.MethodName);
        Assert.Equal(0, handler.DirectKnownExceptionCount);
    }

    [Fact]
    public void Discovery_recognizes_difficult_command_handler_shapes()
    {
        const string source = """
            interface ICommandHandler<TCommand, TResult> { }
            partial class Outer
            {
                public partial class NestedHandler<TCommand>(int seed)
                    : ICommandHandler<TCommand, string>
                {
                    public string Handle(TCommand command) => $"{seed}:{command}";
                }
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Probe.cs", source)]);

        var handler = Assert.Single(discovered);
        Assert.Equal("NestedHandler", handler.TypeName);
        Assert.Equal("Handle", handler.MethodName);
        Assert.Equal(0, handler.DirectKnownExceptionCount);
    }

    [Fact]
    public void Ledger_rejects_command_handler_exclusion_for_a_difficult_handler_shape()
    {
        const string source = """
            interface ICommandHandler<TCommand, TResult> { }
            partial class Outer
            {
                public partial class NestedHandler<TCommand>(int seed)
                    : ICommandHandler<TCommand, string>
                {
                    public string Handle(TCommand command) => $"{seed}:{command}";
                }
            }
            """;
        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Probe.cs", source)]);
        var handler = Assert.Single(discovered);
        var expected = new BarcodeLabelKnownExceptionSite(
            handler.Path,
            handler.TypeName,
            handler.MethodName,
            0,
            BarcodeLabelKnownExceptionSiteKind.Excluded,
            "探针豁免");

        var violations = BarcodeLabelUserMessageSourceAnalyzer.ValidateLedger(
            discovered,
            [expected],
            expectedExclusionCount: 1);

        Assert.Contains(violations, violation => violation.Contains("ICommandHandler 不得豁免", StringComparison.Ordinal));
    }

    [Fact]
    public void Discovery_owns_known_exception_creations_by_their_member_declaration()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            class Probe(string value = "default")
            {
                private readonly Exception field = new KnownException("字段失败。");
                public Probe() : this("value") { throw new KnownException("构造失败。"); }
                public string Property => throw new KnownException("属性失败。");
                public string this[int index] => throw new KnownException("索引失败。");
                public static Probe operator +(Probe left, Probe right) =>
                    throw new KnownException("运算失败。");
                public event Action Changed
                {
                    add => throw new KnownException("订阅失败。");
                    remove => throw new KnownException("退订失败。");
                }
                public void Method()
                {
                    Func<string> nested = () => throw new KnownException("局部失败。");
                    _ = nested();
                }
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Members.cs", source)]);

        Assert.Equal(7, discovered.Count);
        Assert.Equal(8, discovered.Sum(site => site.DirectKnownExceptionCount));
    }

    [Fact]
    public void Discovery_fails_closed_when_a_known_exception_has_no_member_owner()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            throw new KnownException("顶层失败。");
            """;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BarcodeLabelUserMessageSourceAnalyzer.Discover(
                [new BarcodeLabelSourceDocument("Global.cs", source)]));

        Assert.Contains("无法归属到成员", exception.Message, StringComparison.Ordinal);
    }

    private static string SourcePath(string relativePath) => $"{BarcodeLabelSourceRoot}/{relativePath}";

    private static BarcodeLabelKnownExceptionSite Target(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount) =>
        new(path, typeName, methodName, directKnownExceptionCount, BarcodeLabelKnownExceptionSiteKind.Target, "同步公开 BarcodeLabel transportVisible 路径");

    private static BarcodeLabelKnownExceptionSite Excluded(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        string reason) =>
        new(path, typeName, methodName, directKnownExceptionCount, BarcodeLabelKnownExceptionSiteKind.Excluded, reason);

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
