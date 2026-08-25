namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesKnownExceptionMessageArchitectureTests
{
    private const string MesSourceRoot = "backend/services/Business/Mes/src";

    private static readonly IReadOnlyList<MesKnownExceptionSite> ExpectedLedger =
    [
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs", "OperationTask", "Assign", 1, "同步公开手工派工拒绝"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs", "OperationTask", "ApplyScheduleAssignment", 1, "OperationTask 调度事件排除"),
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/QualityAggregate/QualityHoldContext.cs", "QualityHoldContext", "ForceRelease", 1, "同步公开质量保留强制释放拒绝"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs", "ApplicationDbContext", "DuplicateProductionReportReversal", 1, "已有中文静态消息，非本层英文候选"),
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs", "ApplicationDbContext", "RecoverQualityHoldTransitionReplayAsync", 1, "同步公开质量保留幂等冲突"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/MesDomainRuleGuard.cs", "MesDomainRuleGuard", "Enforce", 2, "dynamic exception.Message 透传"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs", "CreateFinishedGoodsReceiptRequestCommandHandler", "Handle", 7, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs", "RecordProductionReportCommandHandler", "Handle", 9, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs", "RetryFinishedGoodsReceiptInventoryPostingCommandHandler", "Handle", 2, "含 dynamic exception.Message 透传"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs", "ReverseProductionReportCommandHandler", "Handle", 7, "已有中文静态消息，非本层英文候选"),
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/TelemetryProductionReportCandidateCommands.cs", "DismissTelemetryProductionReportCandidateCommandHandler", "Handle", 1, "同步公开遥测报工候选操作"),
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/TelemetryProductionReportCandidateCommands.cs", "PromoteTelemetryProductionReportCandidateCommandHandler", "Handle", 1, "同步公开遥测报工候选操作"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialRequirementSnapshotProvider.cs", "HttpMesProductEngineeringMaterialRequirementSnapshotProvider", "GetUomConversionsAsync", 1, "稳定错误码 MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialRequirementSnapshotProvider.cs", "HttpMesProductEngineeringMaterialRequirementSnapshotProvider", "SendAsync", 2, "稳定错误码与 provider 失败透传"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialRequirementSnapshotProvider.cs", "HttpMesProductEngineeringMaterialRequirementSnapshotProvider", "SendOptionalAsync", 1, "稳定错误码与 provider 失败透传"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialRequirementSnapshotProvider.cs", "HttpMesProductEngineeringMaterialRequirementSnapshotProvider", "SendRequestAsync", 2, "稳定错误码与 provider 失败透传"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialSupplyLocationResolver.cs", "InventoryMesMaterialSupplyLocationResolver", "GetAvailabilityAsync", 3, "稳定错误码与 Inventory provider 失败透传"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialSupplyLocationResolver.cs", "InventoryMesMaterialSupplyLocationResolver", "ResolveAsync", 1, "稳定错误码 MATERIAL_SUPPLY_LOCATION_UNCONFIGURED"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialSupplyLocationResolver.cs", "InventoryMesMaterialSupplyLocationResolver", "SelectSourceAllocationsAsync", 1, "稳定错误码 MATERIAL_SOURCE_LOCATION_UNAVAILABLE"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesRoutingSnapshotProvider.cs", "HttpMesProductEngineeringRoutingSnapshotProvider", "SendOptionalAsync", 3, "稳定错误码与 ProductEngineering provider 失败透传"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesRoutingSnapshotProvider.cs", "MesRoutingSnapshotMissingException", ".ctor", 1, "稳定错误码 ROUTING_SNAPSHOT_MISSING"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Quality/MesQualityInspectionPlanClient.cs", "MesQualityInspectionPlanClient", "HasActiveOperationPlanAsync", 4, "稳定错误码 QUALITY_PLAN_SOURCE_UNAVAILABLE 与 Quality provider 失败透传"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "AcceptShiftHandoverCommandHandler", "Handle", 2, "其中 1 处为中文静态消息，另 1 处为 dynamic exception.Message 透传，非本层静态目标"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "AssignDispatchTaskCommandHandler", "Handle", 3, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ChangeOperationTaskStateCommandHandler", "EnsurePreviousOperationsCompletedAsync", 1, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ChangeOperationTaskStateCommandHandler", "Handle", 4, "已有中文静态消息，非本层英文候选"),
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ChangeOperationTaskStateCommandHandler", "TryGetReplayAsync", 1, "同步公开工序动作幂等回执拒绝"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ConfirmDowntimeRecoveryCommandHandler", "Handle", 1, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ConfirmLineSideMaterialReceiptCommandHandler", "Handle", 1, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ConvertPlanToWorkOrderCommandHandler", "CreateWorkOrderAsync", 2, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "CreateMaterialIssueRequestCommandHandler", "Handle", 9, "已有中文静态消息，非本层英文候选"),
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ForceReleaseQualityHoldCommandHandler", "Handle", 2, "同步公开质量保留强制释放拒绝"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "RecordDefectCommandHandler", "Handle", 1, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ReleaseWorkOrderCommandHandler", "Handle", 6, "稳定错误码与 readiness code 排除"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ReturnLineSideMaterialCommandLock", "GetLockKeysAsync", 1, "退料分布式锁归一化查询的稳定异常不属于用户消息目标"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ReturnLineSideMaterialCommandHandler", "Handle", 3, "line-side returns deferred 排除"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "WorkOrderCancellationOrchestrator", "CancelAsync", 2, "dynamic exception.Message 透传，属于动态消息盲区，非本层静态目标"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "WorkOrderLifecycleCommandGuards", "GetWorkOrderAsync", 1, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesFinishedGoodsReceiptLocationResolver.cs", "ConfiguredMesFinishedGoodsReceiptLocationResolver", "Resolve", 1, "稳定错误码 FINISHED_GOODS_LOCATION_UNCONFIGURED"),
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/MasterData/MesSkuAvailabilityGate.cs", "DisabledMesSkuException", ".ctor", 1, "同步公开新建 MES 工单闸门"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/ProductEngineering/MesEngineeringChangeCommands.cs", "MesArchivedProductionVersionGuard", "ThrowIfArchivedAsync", 1, "Engineering Change deferred 排除"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/ProductEngineering/MesEngineeringChangeCommands.cs", "RecordEngineeringChangeDecisionCommandHandler", "Handle", 3, "Engineering Change deferred 与 dynamic 透传排除"),
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Production/MesProductionQueries.cs", "GetProductionReportQueryHandler", "Handle", 1, "同步公开报工详情查询"),
        Target("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Production/TelemetryProductionReportCandidateQueries.cs", "GetTelemetryProductionReportCandidateQueryHandler", "Handle", 1, "同步公开遥测报工候选查询"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs", "GetMaterialReadinessQueryHandler", "Handle", 1, "dynamic readiness message 透传"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs", "GetMesWorkOrderDetailQueryHandler", "Handle", 1, "已有中文静态消息，非本层英文候选"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs", "MesAuthenticatedActor", "Resolve", 3, "MesEndpoints internal/header 分支排除"),
        Excluded("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs", "MesQualityHoldRequestContext", "Resolve", 1, "MesEndpoints internal/header 分支排除"),
    ];

    [Fact]
    public void Static_target_and_exclusion_ledger_matches_the_current_MES_source()
    {
        var documents = ReadMesSourceDocuments();
        var discovered = MesKnownExceptionUserMessageSourceAnalyzer.Discover(documents);

        Assert.Equal(48, discovered.Count);
        Assert.Equal(107, discovered.Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(105, documents.Sum(document => CountOccurrences(document.Text, "new KnownException")));
        Assert.Equal(ExpectedLedger.Count, discovered.Count);

        var expectedByKey = ExpectedLedger.ToDictionary(site => site.Key, StringComparer.Ordinal);
        var discoveredByKey = discovered.ToDictionary(site => site.Key, StringComparer.Ordinal);
        Assert.Equal(expectedByKey.Keys.OrderBy(key => key, StringComparer.Ordinal), discoveredByKey.Keys.OrderBy(key => key, StringComparer.Ordinal));
        foreach (var expected in ExpectedLedger)
        {
            Assert.Equal(expected.DirectKnownExceptionCount, discoveredByKey[expected.Key].DirectKnownExceptionCount);
        }

        var excluded = ExpectedLedger.Where(site => site.Kind == MesKnownExceptionSiteKind.Excluded).ToArray();
        var violations = MesKnownExceptionUserMessageSourceAnalyzer.Analyze(documents, excluded);
        Assert.Empty(violations);
        Assert.Equal(11, ExpectedLedger.Where(site => site.Kind == MesKnownExceptionSiteKind.Target).Sum(site => site.DirectKnownExceptionCount));
    }

    [Fact]
    public void Analyzer_supports_explicit_target_typed_and_primary_constructor_sites()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;

            sealed class Primary(string message) : KnownException(message)
            {
            }

            static class Sample
            {
                public static KnownException Explicit() => new KnownException("English message");
                public static KnownException TargetTyped() => new("English message");
            }
            """;
        var documents = new[] { new MesSourceDocument("synthetic/MesMessages.cs", source) };

        var discovered = MesKnownExceptionUserMessageSourceAnalyzer.Discover(documents);
        Assert.Equal(3, discovered.Count);
        Assert.Contains(discovered, site => site.TypeName == "Primary" && site.MemberName == ".ctor");
        Assert.Contains(discovered, site => site.TypeName == "Sample" && site.MemberName == "Explicit");
        Assert.Contains(discovered, site => site.TypeName == "Sample" && site.MemberName == "TargetTyped");

        var violations = MesKnownExceptionUserMessageSourceAnalyzer.Analyze(documents, []);
        Assert.Equal(3, violations.Count);
        Assert.Equal(2, violations.Count(violation => violation.Contains("中文", StringComparison.Ordinal)));
        Assert.Single(violations, violation => violation.Contains("静态分析", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyzer_fails_closed_for_dynamic_messages_and_honors_exact_exclusions()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;

            static class Sample
            {
                public static KnownException Dynamic(string message) => new KnownException(message);
                public static KnownException Safe() => new KnownException("中文拒绝消息。");
            }
            """;
        var documents = new[] { new MesSourceDocument("synthetic/MesDynamic.cs", source) };
        var discovered = MesKnownExceptionUserMessageSourceAnalyzer.Discover(documents);
        var dynamicSite = Assert.Single(discovered, site => site.MemberName == "Dynamic");

        var violations = MesKnownExceptionUserMessageSourceAnalyzer.Analyze(documents, []);
        Assert.Contains(violations, violation => violation.Contains("静态分析", StringComparison.Ordinal));
        Assert.Empty(MesKnownExceptionUserMessageSourceAnalyzer.Analyze(documents, [dynamicSite]));
    }

    private static MesSourceDocument[] ReadMesSourceDocuments()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, MesSourceRoot.Replace('/', Path.DirectorySeparatorChar));
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(string.Concat(Path.DirectorySeparatorChar, "bin", Path.DirectorySeparatorChar), StringComparison.Ordinal)
                && !file.Contains(string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar), StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(file => new MesSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static MesKnownExceptionSite Target(
        string path,
        string typeName,
        string memberName,
        int count,
        string reason) =>
        new(path, typeName, memberName, count, MesKnownExceptionSiteKind.Target, reason);

    private static MesKnownExceptionSite Excluded(
        string path,
        string typeName,
        string memberName,
        int count,
        string reason) =>
        new(path, typeName, memberName, count, MesKnownExceptionSiteKind.Excluded, reason);

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
