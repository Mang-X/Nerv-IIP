using Nerv.IIP.Testing;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationKnownExceptionMessageArchitectureTests
{
    private const string InfrastructureRoot =
        "backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure";
    private const string WebRoot =
        "backend/services/Notification/src/Nerv.IIP.Notification.Web";

    private static readonly IReadOnlyCollection<ExcludedUserMessageSite> ExcludedSites =
    [
        new($"{WebRoot}/Endpoints/Notifications/NotificationEndpointContext.cs", "NotificationEndpointContext", "RequiredHeader", "网关已完成组织与环境请求头前置校验"),
        new($"{InfrastructureRoot}/DeliveryAttempt.cs", "DeliveryAttempt", "MarkFailed", "后台投递链路，无同步公开端点"),
        new($"{InfrastructureRoot}/DeliveryAttempt.cs", "DeliveryAttempt", "StartRetry", "后台投递链路，无同步公开端点"),
        new($"{InfrastructureRoot}/DeliveryAttempt.cs", "DeliveryAttempt", "EnsureStarted", "后台投递链路，无同步公开端点"),
        new($"{InfrastructureRoot}/DeliveryAttempt.cs", "DeliveryAttempt", "Required", "后台投递链路，无同步公开端点"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "ApprovalStepOverdueIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "ApprovalStepResolvedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "ApprovalActionRecordedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "ApprovalRejectedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/ApprovalIntegrationEventHandlersForNotification.cs", "NotificationIntegrationEventRequired", "Value", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/OperationTaskFailedIntegrationEventHandlerForNotification.cs", "OperationTaskFailedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/OperationTaskFailedIntegrationEventHandlerForNotification.cs", "OperationTaskFailedIntegrationEventHandlerForNotification", "Required", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/SchedulingIntegrationEventHandlersForNotification.cs", "ScheduleConflictDetectedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/SchedulingIntegrationEventHandlersForNotification.cs", "ScheduleConflictDetectedIntegrationEventHandlerForNotification", "Required", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/SchedulingIntegrationEventHandlersForNotification.cs", "SchedulePlanInvalidatedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/SchedulingIntegrationEventHandlersForNotification.cs", "SchedulePlanInvalidatedIntegrationEventHandlerForNotification", "Required", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OperationTaskCompletedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OperationApprovalRequestedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OperationApprovalApprovedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OperationApprovalRejectedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs", "OpsNotificationConsumer", "Required", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/MesEngineeringChangeIntegrationEventHandlersForNotification.cs", "WorkOrderEngineeringChangeImpactDetectedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/MesEngineeringChangeIntegrationEventHandlersForNotification.cs", "WorkOrderEngineeringChangeImpactDetectedIntegrationEventHandlerForNotification", "Required", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/MesAndonIntegrationEventHandlerForNotification.cs", "AndonCallEscalatedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/MesAndonIntegrationEventHandlerForNotification.cs", "AndonCallEscalatedIntegrationEventHandlerForNotification", "Required", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs", "AlarmRaisedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs", "AlarmClearedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs", "AlarmEscalatedIntegrationEventHandlerForNotification", "HandleValidEventAsync", "IntegrationEventHandlers 异步消费链路"),
        new($"{WebRoot}/Application/IntegrationEventHandlers/IndustrialTelemetryAlarmIntegrationEventHandlersForNotification.cs", "IndustrialTelemetryAlarmNotification", "Required", "IntegrationEventHandlers 异步消费链路"),
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
            _ => "用户消息不能包含不安全字符。",
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

        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument(path, source)],
            [new ExcludedUserMessageSite(path, "NotificationEndpointContext", "RequiredHeader", "网关前置校验")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Empty_source_collection_fails_closed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            KnownExceptionUserMessageSourceAnalyzer.Analyze([], []));

        Assert.Contains("源集合不能为空", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Notification_transport_visible_known_exceptions_use_safe_user_messages()
    {
        var documents = ReadSourceDocuments();
        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(documents, ExcludedSites, UserMessagePolicy.RequiredSafeText);
        Assert.True(
            violations.Count == 0,
            "Notification 同步公开 KnownException 用户消息必须静态、含中文、长度不超过 60 且不含不安全符号。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<string> AnalyzeProbe(string source) =>
        KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument("Probe.cs", source)],
            []);

    private static IReadOnlyCollection<UserMessageSourceDocument> ReadSourceDocuments()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "backend", "services", "Notification", "src");
        return Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => file
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .All(part => part is not "bin" and not "obj"))
            .Select(file => new UserMessageSourceDocument(
                Path.GetRelativePath(repositoryRoot, file),
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
