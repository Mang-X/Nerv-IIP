namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityKnownExceptionMessageArchitectureTests
{
    private const string QualityDomainRoot =
        "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain";

    private const string QualityWebRoot =
        "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web";

    private static readonly IReadOnlyCollection<QualityKnownExceptionSite> ExpectedSites =
    [
        Target($"{QualityDomainRoot}/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs", "InspectionRecord", "CreateFromPlan", 3),
        Target($"{QualityDomainRoot}/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs", "InspectionRecord", "CalculatePlannedLines", 1),
        Target($"{QualityDomainRoot}/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs", "InspectionRecord", "CalculatePlannedLine", 2),
        Target($"{QualityDomainRoot}/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs", "InspectionRecord", "CalculateVariableLine", 1),

        Target($"{QualityWebRoot}/Application/Commands/CorrectiveActions/CorrectiveActionCommands.cs", "OpenCorrectiveActionCommandHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Commands/CorrectiveActions/CorrectiveActionCommands.cs", "AddCorrectiveActionItemCommandHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Commands/CorrectiveActions/CorrectiveActionCommands.cs", "CompleteCorrectiveActionItemCommandHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Commands/CorrectiveActions/CorrectiveActionCommands.cs", "VerifyCorrectiveActionEffectivenessCommandHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Commands/CorrectiveActions/CorrectiveActionCommands.cs", "VerifyCorrectiveActionEffectivenessCommandHandler", "ResolveEffectivenessInspectionAsync", 4),
        Target($"{QualityWebRoot}/Application/Commands/CorrectiveActions/CorrectiveActionCommands.cs", "CloseCorrectiveActionCommandHandler", "Handle", 3),

        Target($"{QualityWebRoot}/Application/Commands/InspectionPlans/ActivateInspectionPlanCommand.cs", "ActivateInspectionPlanCommandHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Commands/InspectionPlans/CreateInspectionPlanCommand.cs", "CreateInspectionPlanCommandHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Commands/InspectionRecords/CreateInspectionRecordCommand.cs", "CreateInspectionRecordCommandHandler", "Handle", 2),
        Target($"{QualityWebRoot}/Application/Commands/InspectionRecords/CreateInspectionRecordCommand.cs", "CreateInspectionRecordCommandHandler", "ResolveMeasuringDeviceUsageAsync", 4),
        Target($"{QualityWebRoot}/Application/Commands/InspectionRecords/CreateInspectionRecordCommand.cs", "CreateInspectionRecordCommandHandler", "VerifySourceDocumentAsync", 3),
        Target($"{QualityWebRoot}/Application/Commands/InspectionRecords/CreateReinspectionCommand.cs", "CreateReinspectionCommandHandler", "Handle", 3),
        Target($"{QualityWebRoot}/Application/Commands/InspectionRecords/CreateReinspectionCommand.cs", "CreateReinspectionCommandHandler", "ResolveMeasuringDeviceUsageAsync", 4),
        Target($"{QualityWebRoot}/Application/Commands/InspectionRecords/OpenNcrFromInspectionCommand.cs", "OpenNcrFromInspectionCommandHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Commands/InspectionTasks/CreateInspectionRecordFromTaskCommand.cs", "CreateInspectionRecordFromTaskCommandHandler", "Handle", 2),
        Target($"{QualityWebRoot}/Application/Commands/InspectionTasks/CreateInspectionRecordFromTaskCommand.cs", "CreateInspectionRecordFromTaskCommandHandler", "TryGetReplayAsync", 2),
        Target($"{QualityWebRoot}/Application/Commands/InspectionTasks/CreateInspectionRecordFromTaskCommand.cs", "CreateInspectionRecordFromTaskCommandHandler", "EnsureNcrAndBuildResultAsync", 1),
        Target($"{QualityWebRoot}/Application/Commands/MeasuringDevices/MeasuringDeviceCommands.cs", "RecordMeasuringDeviceCalibrationCommandHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Commands/MeasuringDevices/MeasuringDeviceCommands.cs", "ChangeMeasuringDeviceStatusCommandHandler", "Handle", 2),
        Target($"{QualityWebRoot}/Application/Commands/NonconformanceReports/CloseNonconformanceReportCommand.cs", "CloseNonconformanceReportCommandHandler", "Handle", 4),
        Target($"{QualityWebRoot}/Application/Commands/NonconformanceReports/CreateNonconformanceReportCommand.cs", "CreateNonconformanceReportCommandHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Commands/NonconformanceReports/SubmitNonconformanceReportDispositionCommand.cs", "SubmitNonconformanceReportDispositionCommandHandler", "Handle", 5),
        Target($"{QualityWebRoot}/Application/Commands/QualityReasons/QualityReasonCommands.cs", "CreateQualityReasonCommandHandler", "Handle", 2),
        Target($"{QualityWebRoot}/Application/Commands/QualityReasons/QualityReasonCommands.cs", "UpdateQualityReasonCommandHandler", "FindAsync", 1),

        Target($"{QualityWebRoot}/Application/Queries/CorrectiveActions/CorrectiveActionQueries.cs", "GetCorrectiveActionQueryHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Queries/InspectionRecords/GetInspectionRecordQuery.cs", "GetInspectionRecordQueryHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Queries/InspectionTasks/GetInspectionTaskQuery.cs", "GetInspectionTaskQueryHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Queries/NonconformanceReports/GetNonconformanceReportQuery.cs", "GetNonconformanceReportQueryHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Queries/QualityReasons/QualityReasonQueries.cs", "GetQualityReasonQueryHandler", "Handle", 1),
        Target($"{QualityWebRoot}/Application/Queries/Spc/SpcAnalysisQueries.cs", "SpcDataProjection", "LoadSpecificationAsync", 1),
        Target($"{QualityWebRoot}/Application/Queries/Spc/SpcAnalysisQueries.cs", "SpcCalculation", "CalculateLimits", 2),
        Target($"{QualityWebRoot}/Application/Queries/Spc/SpcAnalysisQueries.cs", "SpcCalculation", "EstimateWithinSubgroupStandardDeviation", 1),
    ];

    [Fact]
    public void Quality_transport_visible_known_exceptions_have_a_closed_target_and_exclusion_ledger()
    {
        var documents = LoadDocuments();
        var discovered = QualityUserMessageSourceAnalyzer.Discover(documents);
        var expectedKeys = ExpectedSites.Select(site => site.Key).ToArray();

        Assert.All(documents, document => Assert.False(
            string.IsNullOrWhiteSpace(document.Text),
            $"Quality 源文件缺失或为空：{document.Path}"));
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(67, discovered.Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(67, ExpectedSites
            .Where(site => site.Kind == QualityKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(0, ExpectedSites
            .Where(site => site.Kind == QualityKnownExceptionSiteKind.Excluded)
            .Sum(site => site.DirectKnownExceptionCount));

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
            var match = Assert.Single(discovered, site => site.Key == expected.Key);
            Assert.Equal(expected.DirectKnownExceptionCount, match.DirectKnownExceptionCount);
        }

        var violations = QualityUserMessageSourceAnalyzer.Analyze(
            documents,
            ExpectedSites
                .Where(site => site.Kind == QualityKnownExceptionSiteKind.Excluded)
                .Select(site => new QualityExcludedSite(site.Path, site.TypeName, site.MethodName, site.Reason))
                .ToArray());

        Assert.True(
            violations.Count == 0,
            "Quality transportVisible 用户消息必须是静态中文。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Analyzer_rejects_english_and_dynamic_mutations_and_honors_exclusion()
    {
        const string englishSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"Inspection task not found.\"); } }";
        var englishViolations = QualityUserMessageSourceAnalyzer.Analyze(
            [new QualitySourceDocument("Probe.cs", englishSource)],
            []);
        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], englishViolations);

        const string dynamicSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(string message) { throw new KnownException(message); } }";
        var dynamicViolations = QualityUserMessageSourceAnalyzer.Analyze(
            [new QualitySourceDocument("Probe.cs", dynamicSource)],
            []);
        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], dynamicViolations);

        var excluded = QualityUserMessageSourceAnalyzer.Analyze(
            [new QualitySourceDocument(
                "Internal.cs",
                "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal\"); } }")],
            [new QualityExcludedSite("Internal.cs", "Probe", "Handle", "internal/no-facade")]);
        Assert.Empty(excluded);
    }

    private static QualityKnownExceptionSite Target(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount) =>
        new(path, typeName, methodName, directKnownExceptionCount, QualityKnownExceptionSiteKind.Target, "同步公开 Quality transportVisible 路径");

    private static IReadOnlyCollection<QualitySourceDocument> LoadDocuments()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot, QualityDomainRoot.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(repositoryRoot, QualityWebRoot.Replace('/', Path.DirectorySeparatorChar)),
        };
        return sourceRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !HasPathSegment(file, "bin") && !HasPathSegment(file, "obj"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(file => new QualitySourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();
    }

    private static bool HasPathSegment(string path, string segment) =>
        path.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);

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
