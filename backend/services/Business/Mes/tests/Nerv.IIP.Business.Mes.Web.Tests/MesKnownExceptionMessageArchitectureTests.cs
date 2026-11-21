namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesKnownExceptionMessageArchitectureTests
{
    private const string MesSourceRoot = "backend/services/Business/Mes/src";

    private static readonly IReadOnlyList<MesExcludedMessageSite> ExcludedSites =
    [
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/AndonCallAggregate/AndonCall.cs", "AndonCall", "Raise", "安灯服务 API 的中文领域拒绝，Gateway facade deferred #3653"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/AndonCallAggregate/AndonCall.cs", "AndonCall", "Claim", "安灯认领与首次时间的中文拒绝，Gateway facade deferred #3653"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/AndonCallAggregate/AndonCall.cs", "AndonCall", "Close", "安灯关闭所有权与生命周期的中文拒绝，Gateway facade deferred #3653"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/AndonCallAggregate/AndonCall.cs", "AndonCall", "TryEscalate", "安灯升级时限的中文领域拒绝，尚无 HTTP 公开面"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Andon/AndonCallCommands.cs", "RaiseAndonCallCommandHandler", "Handle", "安灯重放授权范围的中文拒绝，Gateway facade deferred #3653"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Andon/AndonCallCommands.cs", "ClaimAndonCallCommandHandler", "Handle", "安灯认领授权范围的中文拒绝，Gateway facade deferred #3653"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Andon/AndonCallCommands.cs", "CloseAndonCallCommandHandler", "Handle", "安灯关闭授权范围的中文拒绝，Gateway facade deferred #3653"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Andon/AndonCallQueries.cs", "GetAndonCallQueryHandler", "Handle", "安灯详情授权范围的中文拒绝，Gateway facade deferred #3653"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Andon/AndonCallQueries.cs", "ListAndonCallsQueryHandler", "Handle", "安灯队列参数的中文拒绝，Gateway facade deferred #3653"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Andon/AndonCallQueries.cs", "AndonCallAccess", "EnsureSourceAsync", "安灯来源与授权范围的中文拒绝，Gateway facade deferred #3653"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs", "OperationTask", "Claim", "PDA 自领任务的中文业务拒绝"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ChangeoverRecordAggregate/ChangeoverRecord.cs", "ChangeoverRecord", "Complete", "换型完成时间与重复完成的中文业务拒绝"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs", "OperationTask", "ApplyScheduleAssignment", "OperationTask 调度事件排除"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs", "ApplicationDbContext", "DuplicateProductionReportReversal", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/MesDomainRuleGuard.cs", "MesDomainRuleGuard", "Enforce", "dynamic exception.Message 透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs", "CreateFinishedGoodsReceiptRequestCommandHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs", "RecordProductionReportCommandHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs", "RetryFinishedGoodsReceiptInventoryPostingCommandHandler", "Handle", "含 dynamic exception.Message 透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs", "ReverseProductionReportCommandHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialRequirementSnapshotProvider.cs", "HttpMesProductEngineeringMaterialRequirementSnapshotProvider", "GetSnapshotAsync", "availability reader 稳定错误码与 provider 失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialRequirementSnapshotProvider.cs", "HttpMesProductEngineeringMaterialRequirementSnapshotProvider", "SendAsync", "稳定错误码与 provider 失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialRequirementSnapshotProvider.cs", "HttpMesProductEngineeringMaterialRequirementSnapshotProvider", "SendOptionalAsync", "稳定错误码与 provider 失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialRequirementSnapshotProvider.cs", "HttpMesProductEngineeringMaterialRequirementSnapshotProvider", "SendRequestAsync", "稳定错误码与 provider 失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesFoundationSourceReader.cs", "HttpMesFoundationSourceReader", "Unavailable", "稳定错误码 FOUNDATION_SOURCE_UNAVAILABLE 与 MasterData/ERP/Inventory 来源失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesMaterialAvailabilityReader.cs", "HttpMesMaterialAvailabilityReader", "GetUomConversionsAsync", "稳定错误码 MATERIAL_REQUIREMENT_SOURCE_UNAVAILABLE"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesMaterialAvailabilityReader.cs", "HttpMesMaterialAvailabilityReader", "SendAsync", "稳定错误码与 Inventory 或 MasterData provider 失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialSupplyLocationResolver.cs", "InventoryMesMaterialSupplyLocationResolver", "GetAvailabilityAsync", "稳定错误码与 Inventory provider 失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialSupplyLocationResolver.cs", "InventoryMesMaterialSupplyLocationResolver", "ResolveAsync", "稳定错误码 MATERIAL_SUPPLY_LOCATION_UNCONFIGURED"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesMaterialSupplyLocationResolver.cs", "InventoryMesMaterialSupplyLocationResolver", "SelectSourceAllocationsAsync", "稳定错误码 MATERIAL_SOURCE_LOCATION_UNAVAILABLE"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkerSkillQualificationGate.cs", "HttpMesWorkerSkillQualificationGate", "EnsureQualifiedAsync", "中文业务拒绝与稳定错误码 WORKER_SKILL_SOURCE_UNAVAILABLE"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkerSkillQualificationGate.cs", "HttpMesWorkerSkillQualificationGate", "Unqualified", "中文人员技能资格拒绝"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkerSkillQualificationGate.cs", "UnconfiguredMesWorkerSkillQualificationGate", "EnsureQualifiedAsync", "稳定错误码 WORKER_SKILL_SOURCE_UNAVAILABLE"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesRoutingSnapshotProvider.cs", "HttpMesProductEngineeringRoutingSnapshotProvider", "SendOptionalAsync", "稳定错误码与 ProductEngineering provider 失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesRoutingSnapshotProvider.cs", "MesRoutingSnapshotMissingException", ".ctor", "稳定错误码 ROUTING_SNAPSHOT_MISSING"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Quality/MesFirstArticleGate.cs", "HttpMesFirstArticleGate", "EnsureBatchReportAllowedAsync", "稳定错误码 FIRST_ARTICLE_SOURCE_UNAVAILABLE 与 Quality 来源失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Quality/MesFirstArticleGate.cs", "HttpMesFirstArticleGate", "EnsureAllows", "中文首件门禁业务拒绝与稳定错误码"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Quality/MesFirstArticleGate.cs", "HttpMesFirstArticleGate", "EnsureDecisionAllows", "中文首件门禁业务拒绝与稳定错误码"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Quality/MesQualityInspectionPlanClient.cs", "MesQualityInspectionPlanClient", "HasActiveOperationPlanAsync", "稳定错误码 QUALITY_PLAN_SOURCE_UNAVAILABLE 与 Quality provider 失败透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "AssignDispatchTaskCommandHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ClaimDispatchTaskCommandHandler", "Handle", "PDA 自领任务的中文静态业务拒绝"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ChangeOperationTaskStateCommandHandler", "EnsurePreviousOperationsCompletedAsync", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ChangeOperationTaskStateCommandHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "AuthorizeAndStartOperationTaskCommandHandler", "EnsureRequired", "授权跳站内部命令的中文字段校验消息"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "AuthorizeAndStartOperationTaskCommandHandler", "Handle", "授权跳站内部命令的中文业务阻塞消息"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ConfirmDowntimeRecoveryCommandHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "CompleteChangeoverCommandHandler", "Handle", "换型记录不存在的中文业务拒绝"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ConfirmLineSideMaterialReceiptCommandHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ConvertPlanToWorkOrderCommandHandler", "CreateWorkOrderAsync", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "CreateMaterialIssueRequestCommandHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "CreateMaterialIssueRequestCommandHandler", "ResolveFrozenMaterialSelectionAsync", "冻结物料候选不存在或审计归属歧义的中文业务拒绝"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "RecordDefectCommandHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ReleaseWorkOrderCommandHandler", "Handle", "稳定错误码与 readiness code 排除"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ReturnLineSideMaterialCommandLock", "GetLockKeysAsync", "退料分布式锁归一化查询的稳定异常不属于用户消息目标"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "ReturnLineSideMaterialCommandHandler", "Handle", "line-side returns deferred 排除"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "WorkOrderCancellationOrchestrator", "CancelAsync", "dynamic exception.Message 透传，属于动态消息盲区，非本层静态目标"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs", "WorkOrderLifecycleCommandGuards", "GetWorkOrderAsync", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.cs", "NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder", "HandleScopedAsync", "稳定错误码 MATERIAL_REQUIREMENT_SNAPSHOT_MISSING"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesFinishedGoodsReceiptLocationResolver.cs", "ConfiguredMesFinishedGoodsReceiptLocationResolver", "Resolve", "稳定错误码 FINISHED_GOODS_LOCATION_UNCONFIGURED"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/ProductEngineering/MesEngineeringChangeCommands.cs", "MesArchivedProductionVersionGuard", "ThrowIfArchivedAsync", "Engineering Change deferred 排除"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/ProductEngineering/MesEngineeringChangeCommands.cs", "RecordEngineeringChangeDecisionCommandHandler", "Handle", "Engineering Change deferred 与 dynamic 透传排除"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs", "GetMaterialReadinessQueryHandler", "Handle", "dynamic readiness message 透传"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/GetBatchMaterialReadinessQuery.cs", "GetBatchMaterialReadinessQueryHandler", "Handle", "同现有单工单读面的中文工单不存在拒绝"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs", "GetMaterialIssueRequestQueryHandler", "Handle", "领料详情在当前组织与环境 scope 内不存在的统一中文拒绝"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs", "GetMesWorkOrderDetailQueryHandler", "Handle", "已有中文静态消息，非本层英文候选"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/PrevalidateMaterialScanQuery.cs", "MaterialScanPrevalidationErrors", "SourceUnavailable", "固定中文外层消息；provider、异常与 HTTP reason 仅进入受控日志"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs", "MesAuthenticatedActor", "Resolve", "MesEndpoints internal/header 分支排除"),
        new("backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs", "MesQualityHoldRequestContext", "Resolve", "MesEndpoints internal/header 分支排除"),
    ];

    [Fact]
    public void Mes_transport_visible_known_exceptions_use_safe_user_messages()
    {
        var violations = MesKnownExceptionUserMessageSourceAnalyzer.Analyze(ReadMesSourceDocuments(), ExcludedSites);
        Assert.Empty(violations);
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
        var dynamicSite = new MesExcludedMessageSite(
            "synthetic/MesDynamic.cs", "Sample", "Dynamic", "仅用于测试精确豁免");

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
