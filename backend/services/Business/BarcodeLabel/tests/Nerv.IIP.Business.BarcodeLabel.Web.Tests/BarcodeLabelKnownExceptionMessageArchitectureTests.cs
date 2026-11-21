using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelKnownExceptionMessageArchitectureTests
{
    private const string BarcodeLabelSourceRoot =
        "backend/services/Business/BarcodeLabel/src";

    private static readonly IReadOnlyCollection<ExcludedUserMessageSite> ExcludedSites =
    [
        new(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "ActivateLabelPrintBatchCommandHandler", "Handle", "Scoped MES activation is deferred to the #2893 coordinator"),
        new(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "LoadBatchAsync", "PrintLabel lifecycle helper is internal/no-facade"),
        new(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "LoadScopedBatchAsync", "Scoped lifecycle helper remains internal until #2975 adds its public facade"),
        new(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "CompileFrozenBatchAsync", "PrintLabel lifecycle helper is internal/no-facade"),
        new(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/TemplateAssetRetirements/CreateTemplateAssetRetirementDecisionCommand.cs"), "CreateTemplateAssetRetirementDecisionCommandHandler", "RejectExpiredAsync", "#3047 requires exact stable replay-window-expired wire code; HTTP retention regression proves transport"),
    ];

    [Fact]
    public void BarcodeLabel_transport_visible_known_exceptions_use_safe_user_messages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, BarcodeLabelSourceRoot.Replace('/', Path.DirectorySeparatorChar));
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
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
            KnownExceptionUserMessageSourceAnalyzer.Analyze(
                [new UserMessageSourceDocument("Probe.cs", englishSource)],
                []));

        const string dynamicSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(string message) { throw new KnownException(message); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"],
            KnownExceptionUserMessageSourceAnalyzer.Analyze(
                [new UserMessageSourceDocument("Probe.cs", dynamicSource)],
                []));

        const string unsafeSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"无法保存 <内容>。\"); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息不能包含不安全字符。"],
            KnownExceptionUserMessageSourceAnalyzer.Analyze(
                [new UserMessageSourceDocument("Probe.cs", unsafeSource)],
                []));

        var excluded = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument(
                "Internal.cs",
                "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal-code\"); } }")],
            [new ExcludedUserMessageSite("Internal.cs", "Probe", "Handle", "internal/no-facade")]);

        Assert.Empty(excluded);
    }

    private static string SourcePath(string relativePath) => $"{BarcodeLabelSourceRoot}/{relativePath}";

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
