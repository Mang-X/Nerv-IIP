namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelKnownExceptionMessageArchitectureTests
{
    private const string BarcodeLabelSourceRoot =
        "backend/services/Business/BarcodeLabel/src";

    private static readonly IReadOnlyCollection<string> SourcePaths =
    [
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Infrastructure/ApplicationDbContext.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/BarcodeRules/CreateOrUpdateBarcodeRuleCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/CreateLabelPrintBatchCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/Scans/RecordScanCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/PrintBatches/GetLabelPrintBatchQuery.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/ListQueryCriteria.cs",
    ];

    private static readonly IReadOnlyCollection<BarcodeLabelKnownExceptionSite> ExpectedSites =
    [
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Infrastructure/ApplicationDbContext.cs"), "ApplicationDbContext", "TryMapUniqueConflict", 2),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/BarcodeRules/CreateOrUpdateBarcodeRuleCommand.cs"), "CreateOrUpdateBarcodeRuleCommandHandler", "Handle", 4),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/CreateLabelPrintBatchCommand.cs"), "CreateLabelPrintBatchCommandHandler", "Handle", 3),
        Excluded(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "ReprintLabelCommandHandler", "Handle", 1, "PrintLabel lifecycle endpoint is internal/no-facade"),
        Excluded(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "LoadBatchAsync", 1, "PrintLabel lifecycle helper is internal/no-facade"),
        Excluded(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "LoadScopedBatchAsync", 1, "Scoped lifecycle helper remains internal until #2975 adds its public facade"),
        Excluded(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "FindScopedItem", 1, "Scoped lifecycle helper remains internal until #2975 adds its public facade"),
        Excluded(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "RequiredJobId", 1, "PrintLabel lifecycle helper is internal/no-facade"),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/Scans/RecordScanCommand.cs"), "RecordScanCommandHandler", "Handle", 6),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/PrintBatches/GetLabelPrintBatchQuery.cs"), "GetLabelPrintBatchQueryHandler", "Handle", 1),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/ListQueryCriteria.cs"), "TenantScope", "From", 2),
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
        Assert.Equal(18, ExpectedSites.Where(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(5, ExpectedSites.Where(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Excluded)
            .Sum(site => site.DirectKnownExceptionCount));

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(documents);
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
            "BarcodeLabel KnownException 位点未被 target/exclusion ledger 分类："
            + Environment.NewLine
            + string.Join(Environment.NewLine, unclassified));
        Assert.Equal(expectedKeySet.Count, discovered.Count);

        foreach (var expected in ExpectedSites)
        {
            var matches = discovered.Where(site => site.Key == expected.Key).ToArray();
            Assert.Single(matches);
            Assert.Equal(expected.DirectKnownExceptionCount, matches[0].DirectKnownExceptionCount);
        }

        var violations = BarcodeLabelUserMessageSourceAnalyzer.Analyze(
            documents,
            ExpectedSites
                .Where(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Excluded)
                .Select(site => new BarcodeLabelExcludedSite(site.Path, site.TypeName, site.MethodName, site.Reason))
                .ToArray());

        Assert.True(
            violations.Count == 0,
            "BarcodeLabel transportVisible 用户消息必须是中文、静态可分析、长度不超过 60 且不含危险字符。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Analyzer_reports_dynamic_messages_unsafe_messages_and_honors_exclusion()
    {
        const string englishSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"Barcode failed.\"); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息必须包含中文。"],
            BarcodeLabelUserMessageSourceAnalyzer.Analyze(
                [new BarcodeLabelSourceDocument("Probe.cs", englishSource)],
                []));

        const string dynamicSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(string message) { throw new KnownException(message); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"],
            BarcodeLabelUserMessageSourceAnalyzer.Analyze(
                [new BarcodeLabelSourceDocument("Probe.cs", dynamicSource)],
                []));

        const string unsafeSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"无法保存 <内容>。\"); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息不能包含不安全字符。"],
            BarcodeLabelUserMessageSourceAnalyzer.Analyze(
                [new BarcodeLabelSourceDocument("Probe.cs", unsafeSource)],
                []));

        var excluded = BarcodeLabelUserMessageSourceAnalyzer.Analyze(
            [new BarcodeLabelSourceDocument(
                "Internal.cs",
                "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal-code\"); } }")],
            [new BarcodeLabelExcludedSite("Internal.cs", "Probe", "Handle", "internal/no-facade")]);

        Assert.Empty(excluded);
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
