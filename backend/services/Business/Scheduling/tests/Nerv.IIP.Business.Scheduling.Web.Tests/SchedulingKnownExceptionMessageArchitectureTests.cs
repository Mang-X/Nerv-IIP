namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingKnownExceptionMessageArchitectureTests
{
    private const string SchedulingWebRoot =
        "backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web";

    private static readonly IReadOnlyCollection<string> SourcePaths =
    [
        $"{SchedulingWebRoot}/Application/Commands/SchedulingWorkbenchCommands.cs",
        $"{SchedulingWebRoot}/Application/Commands/UpsertScheduleOperationOverrideCommand.cs",
        $"{SchedulingWebRoot}/Application/Commands/ReleaseSchedulePlanCommand.cs",
        $"{SchedulingWebRoot}/Application/Commands/AssembleSchedulingProblemCommand.cs",
        $"{SchedulingWebRoot}/Application/Commands/RevokeSchedulePlanCommand.cs",
        $"{SchedulingWebRoot}/Application/Commands/CreateSchedulePlanCommand.cs",
        $"{SchedulingWebRoot}/Application/Queries/SchedulingQueries.cs",
        $"{SchedulingWebRoot}/Application/IntegrationEventConverters/SchedulingIntegrationEventConverters.cs",
        $"{SchedulingWebRoot}/Application/Scheduling/SchedulingWorkbenchSourceProvider.cs",
        $"{SchedulingWebRoot}/Application/Scheduling/SchedulingProblemProducer.cs",
        $"{SchedulingWebRoot}/Application/Urgency/OrderUrgencyApplication.cs",
    ];

    private static readonly IReadOnlyCollection<SchedulingKnownExceptionSite> ExpectedSites =
    [
        Target($"{SchedulingWebRoot}/Application/Commands/SchedulingWorkbenchCommands.cs", "CreateSchedulePlanRevisionCommandHandler", "Handle", 3),
        Target($"{SchedulingWebRoot}/Application/Commands/SchedulingWorkbenchCommands.cs", "CreateSchedulePlanRevisionCommandHandler", "ValidateLocks", 3),
        Target($"{SchedulingWebRoot}/Application/Commands/UpsertScheduleOperationOverrideCommand.cs", "UpsertScheduleOperationOverrideCommandHandler", "Handle", 6),
        Target($"{SchedulingWebRoot}/Application/Commands/ReleaseSchedulePlanCommand.cs", "ReleaseSchedulePlanUniqueConflictBehavior", "Handle", 1),
        Target($"{SchedulingWebRoot}/Application/Commands/ReleaseSchedulePlanCommand.cs", "ReleaseSchedulePlanCommandHandler", "Handle", 4),
        Excluded($"{SchedulingWebRoot}/Application/Commands/AssembleSchedulingProblemCommand.cs", "AssembleSchedulingProblemCommandHandler", "Handle", 3, "deferred/no-facade；若 facade matrix 转为 exposed，必须重新分类"),
        Target($"{SchedulingWebRoot}/Application/Commands/RevokeSchedulePlanCommand.cs", "RevokeSchedulePlanCommandHandler", "Handle", 2),
        Target($"{SchedulingWebRoot}/Application/Commands/CreateSchedulePlanCommand.cs", "CreateSchedulePlanCommandHandler", "Handle", 2),
        Target($"{SchedulingWebRoot}/Application/Queries/SchedulingQueries.cs", "GetSchedulePlanDetailQueryHandler", "Handle", 1),
        Target($"{SchedulingWebRoot}/Application/Queries/SchedulingQueries.cs", "GetSchedulePlanGanttQueryHandler", "Handle", 1),
        Target($"{SchedulingWebRoot}/Application/IntegrationEventConverters/SchedulingIntegrationEventConverters.cs", "HttpSchedulingIntegrationEventContextAccessor", "ResolveActor", 1),
        Target($"{SchedulingWebRoot}/Application/Scheduling/SchedulingWorkbenchSourceProvider.cs", "HttpSchedulingWorkbenchSourceProvider", "ResolveOrdersAsync", 5),
        Target($"{SchedulingWebRoot}/Application/Scheduling/SchedulingWorkbenchSourceProvider.cs", "HttpSchedulingWorkbenchSourceProvider", "ReadMesWorkOrderListResponseAsync", 5),
        Target($"{SchedulingWebRoot}/Application/Scheduling/SchedulingProblemProducer.cs", "HttpSchedulingProblemProductEngineeringClient", "ParseVersionId", 1),
        Target($"{SchedulingWebRoot}/Application/Scheduling/SchedulingProblemProducer.cs", "HttpSchedulingProblemMasterDataClient", "GetWorkCenterAsync", 1),
        Target($"{SchedulingWebRoot}/Application/Scheduling/SchedulingProblemProducer.cs", "HttpSchedulingProblemMasterDataClient", "ListDeviceAssetsAsync", 1),
        Target($"{SchedulingWebRoot}/Application/Scheduling/SchedulingProblemProducer.cs", "HttpSchedulingProblemMasterDataClient", "ListShiftDetailsAsync", 1),
        Target($"{SchedulingWebRoot}/Application/Urgency/OrderUrgencyApplication.cs", "OrderUrgencyService", "SetBusinessPriorityAsync", 1),
        Target($"{SchedulingWebRoot}/Application/Urgency/OrderUrgencyApplication.cs", "OrderUrgencyPriorityConflictBehavior", "Handle", 2),
    ];

    [Fact]
    public void Scheduling_transport_visible_known_exceptions_have_a_closed_target_and_exclusion_ledger()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(
            repositoryRoot,
            SchedulingWebRoot.Replace('/', Path.DirectorySeparatorChar));
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        var documents = sourceFiles
            .Select(file => new SchedulingSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();

        var documentPaths = documents.Select(document => document.Path).ToHashSet(StringComparer.Ordinal);
        Assert.All(SourcePaths, path => Assert.Contains(path, documentPaths));
        Assert.All(documents, document => Assert.False(string.IsNullOrEmpty(document.Text), $"Scheduling 源文件缺失或为空：{document.Path}"));

        var expectedKeys = ExpectedSites.Select(site => site.Key).ToArray();
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(41, ExpectedSites.Where(site => site.Kind == SchedulingKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(3, ExpectedSites.Where(site => site.Kind == SchedulingKnownExceptionSiteKind.Excluded)
            .Sum(site => site.DirectKnownExceptionCount));

        var discovered = SchedulingUserMessageSourceAnalyzer.Discover(documents);
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
        Assert.Empty(unclassified);
        Assert.Equal(expectedKeySet.Count, discovered.Count);

        foreach (var expected in ExpectedSites)
        {
            var matches = discovered.Where(site => site.Key == expected.Key).ToArray();
            Assert.Single(matches);
            Assert.Equal(expected.DirectKnownExceptionCount, matches[0].DirectKnownExceptionCount);
        }

        var violations = SchedulingUserMessageSourceAnalyzer.Analyze(
            documents,
            ExpectedSites
                .Where(site => site.Kind == SchedulingKnownExceptionSiteKind.Excluded)
                .Select(site => new SchedulingExcludedSite(site.Path, site.TypeName, site.MethodName, site.Reason))
                .ToArray());

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
        var documents = new[] { new SchedulingSourceDocument("Probe.cs", source) };

        var violations = SchedulingUserMessageSourceAnalyzer.Analyze(documents, []);

        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], violations);

        var excluded = SchedulingUserMessageSourceAnalyzer.Analyze(
            [new SchedulingSourceDocument("Deferred.cs", "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal\"); } }")],
            [new SchedulingExcludedSite(
                "Deferred.cs",
                "Probe",
                "Handle",
                "deferred/no-facade；若 facade matrix 转为 exposed，必须重新分类")]);

        Assert.Empty(excluded);
    }

    private static SchedulingKnownExceptionSite Target(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount) =>
        new(path, typeName, methodName, directKnownExceptionCount, SchedulingKnownExceptionSiteKind.Target, "transportVisible synchronous Scheduling path");

    private static SchedulingKnownExceptionSite Excluded(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        string reason) =>
        new(path, typeName, methodName, directKnownExceptionCount, SchedulingKnownExceptionSiteKind.Excluded, reason);

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
