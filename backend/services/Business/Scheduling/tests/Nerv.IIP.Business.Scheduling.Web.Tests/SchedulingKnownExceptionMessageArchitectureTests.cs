using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingKnownExceptionMessageArchitectureTests
{
    private const string SchedulingWebRoot =
        "backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web";

    private static readonly IReadOnlyCollection<ExcludedUserMessageSite> ExcludedSites =
    [
        new($"{SchedulingWebRoot}/Application/Commands/ScheduleWorkingDraftCommands.cs", "SaveScheduleWorkingDraftCommandHandler", "Handle", "deferred/no-facade；#4143 交付 facade 时重新分类"),
        new($"{SchedulingWebRoot}/Endpoints/Scheduling/ScheduleWorkingDraftEndpoints.cs", "ScheduleWorkingDraftIdentity", "Read", "deferred/no-facade；#4143 交付 facade 时重新分类"),
        new($"{SchedulingWebRoot}/Application/Commands/AssembleSchedulingProblemCommand.cs", "AssembleSchedulingProblemCommandHandler", "Handle", "deferred/no-facade；若 facade matrix 转为 exposed，必须重新分类"),
        new($"{SchedulingWebRoot}/Application/Queries/GetScheduleFirstPlanJobQuery.cs", "GetScheduleFirstPlanJobQueryHandler", "Handle", "deferred/no-facade；#4136 交付 facade 时重新分类"),
        new($"{SchedulingWebRoot}/Application/Commands/ScheduleInsertionPreviewJobCommands.cs", "AcceptScheduleInsertionPreviewJobCommandHandler", "Handle", "deferred/no-facade；#4162 交付 facade 时重新分类"),
        new($"{SchedulingWebRoot}/Application/Queries/GetScheduleInsertionPreviewJobQuery.cs", "GetScheduleInsertionPreviewJobQueryHandler", "Handle", "deferred/no-facade；#4162 交付 facade 时重新分类"),
        new($"{SchedulingWebRoot}/Application/Queries/GetMaterialDeliverySourcesQuery.cs", "GetMaterialDeliverySourcesQueryHandler", "Handle", "internal：仅 DemandPlanning 物料交付服务消费，不通过 Gateway 暴露"),
    ];

    [Fact]
    public void Scheduling_transport_visible_known_exceptions_use_safe_user_messages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(
            repositoryRoot,
            SchedulingWebRoot.Replace('/', Path.DirectorySeparatorChar));
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
            "Scheduling transportVisible 用户消息必须是中文、静态可分析、长度不超过 60 且不含危险字符。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Analyzer_reports_dynamic_messages_and_ignores_the_deferred_exclusion()
    {
        const string source = "using NetCorePal.Extensions.Primitives; class Probe { void Handle(string message) { throw new KnownException(message); } }";
        var documents = new[] { new UserMessageSourceDocument("Probe.cs", source) };

        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(documents, []);

        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], violations);

        var excluded = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument("Deferred.cs", "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal\"); } }")],
            [new ExcludedUserMessageSite(
                "Deferred.cs",
                "Probe",
                "Handle",
                "deferred/no-facade；若 facade matrix 转为 exposed，必须重新分类")]);

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
