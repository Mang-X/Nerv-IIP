namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationKnownExceptionMessageArchitectureTests
{
    private const string DomainRoot =
        "backend/services/Notification/src/Nerv.IIP.Notification.Domain";
    private const string InfrastructureRoot =
        "backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure";
    private const string WebRoot =
        "backend/services/Notification/src/Nerv.IIP.Notification.Web";

    private static readonly IReadOnlyCollection<NotificationKnownExceptionSite> TargetSites =
    [
        Target($"{DomainRoot}/AggregatesModel/NotificationIntentAggregate/NotificationIntent.cs", "NotificationIntent", "NotificationIntent", 1, "同步意图提交构造校验"),
        Target($"{DomainRoot}/AggregatesModel/NotificationIntentAggregate/NotificationIntent.cs", "NotificationIntent", "MarkRead", 1, "同步消息已读公开命令"),
        Target($"{DomainRoot}/AggregatesModel/NotificationIntentAggregate/NotificationIntent.cs", "NotificationIntent", "Required", 1, "同步意图字段公开校验"),
        Target($"{DomainRoot}/AggregatesModel/NotificationIntentAggregate/NotificationIntent.cs", "NotificationIntent", "RequiredIntentType", 1, "同步意图类型公开校验"),
        Target($"{DomainRoot}/AggregatesModel/NotificationIntentAggregate/NotificationIntent.cs", "NotificationIntent", "RequiredSeverity", 1, "同步意图严重级别公开校验"),
        Target($"{WebRoot}/Application/Commands/Notifications/MarkNotificationMessageReadCommand.cs", "MarkNotificationMessageReadCommandHandler", "Handle", 1, "同步单条消息已读公开命令"),
        Target($"{WebRoot}/Application/Commands/Notifications/MarkNotificationMessageReadCommand.cs", "MarkNotificationMessageReadCommandHandler", "ParseMessageId", 1, "同步消息标识公开校验"),
        Target($"{WebRoot}/Application/Commands/Notifications/MarkNotificationMessagesReadCommand.cs", "MarkNotificationMessagesReadCommandHandler", "Handle", 1, "同步批量消息已读公开命令"),
        Target($"{WebRoot}/Endpoints/Notifications/NotificationDeadLetterEndpoints.cs", "IgnoreNotificationDeadLetterEndpoint", "HandleAsync", 1, "同步死信忽略公开端点"),
        Target($"{WebRoot}/Endpoints/Notifications/NotificationDeadLetterEndpoints.cs", "NotificationDeadLetterEndpointMapper", "ParseStatus", 1, "同步死信状态公开查询"),
        Target($"{WebRoot}/Endpoints/Notifications/NotificationDeadLetterEndpoints.cs", "NotificationDeadLetterEndpointMapper", "ParseSkip", 1, "同步死信分页公开查询"),
        Target($"{WebRoot}/Endpoints/Notifications/NotificationDeadLetterEndpoints.cs", "NotificationDeadLetterEndpointMapper", "ParseTake", 1, "同步死信分页公开查询"),
        Target($"{InfrastructureRoot}/NotificationPreference.cs", "NotificationPreference", "Required", 1, "同步通知偏好公开配置"),
        Target($"{InfrastructureRoot}/NotificationSubscription.cs", "NotificationSubscription", "Required", 1, "同步通知订阅公开配置"),
        Target($"{InfrastructureRoot}/NotificationRecipientChannelBinding.cs", "NotificationRecipientChannelBinding", "Required", 1, "同步收件渠道绑定公开配置"),
    ];

    private static readonly IReadOnlyCollection<NotificationExcludedKnownExceptionSite> ExcludedSites =
    [
        Excluded($"{WebRoot}/Endpoints/Notifications/NotificationEndpointContext.cs", "NotificationEndpointContext", "RequiredHeader", 1, "网关已完成组织与环境请求头前置校验"),
        Excluded($"{InfrastructureRoot}/DeliveryAttempt.cs", "DeliveryAttempt", "MarkFailed", 1, "后台投递链路，无同步公开端点"),
        Excluded($"{InfrastructureRoot}/DeliveryAttempt.cs", "DeliveryAttempt", "StartRetry", 2, "后台投递链路，无同步公开端点"),
        Excluded($"{InfrastructureRoot}/DeliveryAttempt.cs", "DeliveryAttempt", "EnsureStarted", 1, "后台投递链路，无同步公开端点"),
        Excluded($"{InfrastructureRoot}/DeliveryAttempt.cs", "DeliveryAttempt", "Required", 1, "后台投递链路，无同步公开端点"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "ApprovalStepOverdueIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "ApprovalStepResolvedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "ApprovalActionRecordedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 2, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "ApprovalRejectedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "NotificationIntegrationEventRequired", "Value", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/OperationTaskFailedIntegrationEventHandlerForNotification.cs", "OperationTaskFailedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/OperationTaskFailedIntegrationEventHandlerForNotification.cs", "OperationTaskFailedIntegrationEventHandlerForNotification", "Required", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/SchedulingIntegrationEventHandlersForNotification.cs", "ScheduleConflictDetectedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/SchedulingIntegrationEventHandlersForNotification.cs", "ScheduleConflictDetectedIntegrationEventHandlerForNotification", "Required", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/SchedulingIntegrationEventHandlersForNotification.cs", "SchedulePlanInvalidatedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/SchedulingIntegrationEventHandlersForNotification.cs", "SchedulePlanInvalidatedIntegrationEventHandlerForNotification", "Required", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OperationTaskCompletedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OperationApprovalRequestedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OperationApprovalApprovedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OperationApprovalRejectedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OpsNotificationConsumer", "Required", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/MesEngineeringChangeIntegrationEventHandlersForNotification.cs", "WorkOrderEngineeringChangeImpactDetectedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/MesEngineeringChangeIntegrationEventHandlersForNotification.cs", "WorkOrderEngineeringChangeImpactDetectedIntegrationEventHandlerForNotification", "Required", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs", "AlarmRaisedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs", "AlarmClearedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 1, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs", "AlarmEscalatedIntegrationEventHandlerForNotification", "HandleValidEventAsync", 2, "IntegrationEventHandlers 异步消费链路"),
        Excluded($"{WebRoot}/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs", "IndustrialTelemetryAlarmNotification", "Required", 1, "IntegrationEventHandlers 异步消费链路"),
    ];

    public static TheoryData<string, string> InvalidUserMessageSources => new()
    {
        {
            "using NetCorePal.Extensions.Primitives; class Probe { void Run() { throw new KnownException(\"Unable to save\"); } }",
            "English message"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create(string message) => new(message); }",
            "dynamic message"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create() => new(\"通知失败 <详情>。\"); }",
            "unsafe message"
        },
    };

    [Theory]
    [MemberData(nameof(InvalidUserMessageSources))]
    public void Invalid_user_messages_are_reported(string source, string reason)
    {
        var violations = AnalyzeProbe(source);

        Assert.NotEmpty(violations);
        var expectedReason = reason switch
        {
            "English message" => "用户消息必须包含中文。",
            "dynamic message" => "用户消息必须是可静态分析的字符串字面量或插值字符串。",
            _ => "用户消息不得包含不安全符号。",
        };
        Assert.Contains(violations, violation => violation.EndsWith(expectedReason, StringComparison.Ordinal));
    }

    [Fact]
    public void Chinese_interpolated_user_messages_are_allowed()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create(string code) => new($\"编码 {code} 无效，请检查后重试。\"); }";

        Assert.Empty(AnalyzeProbe(source));
    }

    [Fact]
    public void Same_named_non_framework_exception_is_ignored()
    {
        const string source =
            "namespace Fake { public sealed class KnownException(string message) : System.Exception(message); } class Probe { void Run() { throw new Fake.KnownException(\"Unable to save\"); } }";

        Assert.Empty(AnalyzeProbe(source));
    }

    [Fact]
    public void Excluded_sites_are_ignored_by_exact_file_type_and_method()
    {
        const string path =
            $"{WebRoot}/Endpoints/Notifications/NotificationEndpointContext.cs";
        const string source =
            "using NetCorePal.Extensions.Primitives; class NotificationEndpointContext { string RequiredHeader(string name) => throw new KnownException(name); }";

        var violations = NotificationUserMessageSourceAnalyzer.Analyze(
            [new NotificationSourceDocument(path, source)],
            [Excluded(path, "NotificationEndpointContext", "RequiredHeader", 1, "网关前置校验")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Empty_source_collection_fails_closed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NotificationUserMessageSourceAnalyzer.Analyze([], []));

        Assert.Contains("源集合不能为空", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Notification_direct_constructor_inventory_matches_gate_two_ledger()
    {
        var documents = ReadSourceDocuments();
        var expected = TargetSites
            .Select(site => (site.Key, site.DirectKnownExceptionCount))
            .Concat(ExcludedSites.Select(site => (site.Key, site.DirectKnownExceptionCount)))
            .ToDictionary(item => item.Key, item => item.DirectKnownExceptionCount, StringComparer.Ordinal);
        var discovered = NotificationUserMessageSourceAnalyzer.Discover(documents);
        var actual = discovered.ToDictionary(site => site.Key, site => site.DirectKnownExceptionCount, StringComparer.Ordinal);

        Assert.Equal(15, TargetSites.Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(30, ExcludedSites.Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(45, expected.Values.Sum());
        var missing = expected.Keys.Except(actual.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var extra = actual.Keys.Except(expected.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            missing.Length == 0 && extra.Length == 0,
            $"台账位点不一致。缺少: {string.Join(", ", missing)}；多出: {string.Join(", ", extra)}");
        foreach (var (key, count) in expected)
        {
            Assert.Equal(count, actual[key]);
        }

        var violations = NotificationUserMessageSourceAnalyzer.Analyze(documents, ExcludedSites);
        Assert.True(
            violations.Count == 0,
            "Notification 同步公开 KnownException 用户消息必须静态、含中文、长度不超过 60 且不含不安全符号。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<string> AnalyzeProbe(string source) =>
        NotificationUserMessageSourceAnalyzer.Analyze(
            [new NotificationSourceDocument("Probe.cs", source)],
            []);

    private static IReadOnlyCollection<NotificationSourceDocument> ReadSourceDocuments()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "backend", "services", "Notification", "src");
        return Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => file
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .All(part => part is not "bin" and not "obj"))
            .Select(file => new NotificationSourceDocument(
                Path.GetRelativePath(repositoryRoot, file),
                File.ReadAllText(file)))
            .ToArray();
    }

    private static NotificationKnownExceptionSite Target(
        string path,
        string typeName,
        string methodName,
        int count,
        string reason) =>
        new(path, typeName, methodName, count, reason);

    private static NotificationExcludedKnownExceptionSite Excluded(
        string path,
        string typeName,
        string methodName,
        int count,
        string reason) =>
        new(path, typeName, methodName, count, reason);

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
