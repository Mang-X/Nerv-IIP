using System.Net;
using System.Text;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Web.Application.Approval;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryKnownExceptionMessageArchitectureTests
{
    private const string InventorySourceRoot = "backend/services/Business/Inventory/src";
    private const string InventoryWebRoot = "backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web";
    private const string InventoryInfrastructureRoot = "backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure";

    private static readonly IReadOnlyCollection<InventoryKnownExceptionSite> ExpectedKnownExceptionSites =
    [
        Target($"{InventoryInfrastructureRoot}/ApplicationDbContext.cs", "ApplicationDbContext", "SaveChangesAsync", 1, "公开命令保存边界的并发拒绝"),
        Target($"{InventoryWebRoot}/Application/Approval/StockCountApprovalClient.cs", "HttpStockCountApprovalClient", "StartApprovalAsync", 1, "公开盘点调整 facade 的审批启动同步链"),
        Excluded($"{InventoryWebRoot}/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs", "QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer", "HandleValidEventAsync", 3, "Quality 集成事件消费者，无原始 HTTP facade"),
        Target($"{InventoryWebRoot}/Application/Queries/GetStockAvailabilityQuery.cs", "GetStockAvailabilityQueryHandler", "Handle", 1, "公开库存可用量查询 facade"),
        Target($"{InventoryWebRoot}/Application/Commands/StockCounts/RestartStockCountTaskCommand.cs", "RestartStockCountTaskCommandHandler", "Handle", 3, "公开盘点重盘 facade"),
        Target($"{InventoryWebRoot}/Application/Commands/StockCounts/CreateStockCountTaskCommand.cs", "CreateStockCountTaskCommandHandler", "Handle", 3, "公开盘点任务创建 facade"),
        Excluded($"{InventoryWebRoot}/Application/Commands/StockReservations/ReleaseStockReservationCommand.cs", "ReleaseStockReservationCommandHandler", "Handle", 2, "reservation release 为 internal endpoint"),
        Target($"{InventoryWebRoot}/Application/Commands/StockCounts/CancelStockCountTaskCommand.cs", "CancelStockCountTaskCommandHandler", "Handle", 3, "公开盘点任务取消 facade"),
        Excluded($"{InventoryWebRoot}/Application/Commands/StockCounts/CompleteStockCountAdjustmentApprovalCommand.cs", "CompleteStockCountAdjustmentApprovalCommandHandler", "Handle", 1, "审批回写由集成事件消费者触发，无原始 HTTP facade"),
        Excluded($"{InventoryWebRoot}/Application/Commands/StockReservations/ReserveStockCommand.cs", "ReserveStockCommandHandler", "Handle", 3, "reservation reserve 为 internal endpoint"),
        Excluded($"{InventoryWebRoot}/Application/Commands/StockReservations/ReserveStockCommand.cs", "ReserveFefoStockCommandHandler", "Handle", 3, "reservation FEFO 为 internal endpoint"),
        Target($"{InventoryWebRoot}/Application/Queries/GetStockBySourceQuery.cs", "GetStockBySourceQueryHandler", "Handle", 1, "公开库存来源流水查询 facade"),
        Target($"{InventoryWebRoot}/Application/Commands/StockCounts/ConfirmStockCountAdjustmentCommand.cs", "ConfirmStockCountAdjustmentCommandHandler", "Handle", 6, "公开盘点调整确认 facade"),
        Excluded($"{InventoryWebRoot}/Application/Commands/StockStatusTransfers/PostStockStatusTransferCommand.cs", "PostStockStatusTransferCommandHandler", "Handle", 5, "质量状态转移为 internal endpoint"),
        Excluded($"{InventoryWebRoot}/Application/Commands/StockReservations/RenewStockReservationCommand.cs", "RenewStockReservationCommandHandler", "Handle", 1, "reservation renew 为 internal endpoint"),
    ];

    private static readonly IReadOnlyCollection<InventoryPostingRejectedSite> ExpectedPostingRejectedSites =
    [
        PostingTarget($"{InventoryWebRoot}/Application/Commands/StockMovements/PostStockMovementCommand.cs", "PostStockMovementCommandHandler", "Handle", 4, 3, "公开 postInventoryMovement facade"),
        PostingTarget($"{InventoryWebRoot}/Application/Commands/StockMovements/PostStockMovementCommand.cs", "PostStockMovementCommandHandler", "ValidateTransferLegsOrReject", 7, 0, "公开 postInventoryMovement facade 的调拨校验"),
        PostingTarget($"{InventoryWebRoot}/Application/Commands/StockMovements/PostStockMovementCommand.cs", "PostStockMovementCommandHandler", "CreateTransferInMovementOrReject", 1, 0, "公开 postInventoryMovement facade 的入库腿校验"),
        PostingTarget($"{InventoryWebRoot}/Application/Commands/StockMovements/PostStockMovementCommand.cs", "PostStockMovementCommandHandler", "CreateMovementOrReject", 1, 0, "公开 postInventoryMovement facade 的移动创建校验"),
        PostingTarget($"{InventoryWebRoot}/Application/Commands/StockMovements/PostStockMovementCommand.cs", "PostStockMovementCommandHandler", "DeriveExpiryDate", 1, 0, "公开 postInventoryMovement facade 的失效日期校验"),
        PostingTarget($"{InventoryWebRoot}/Application/Commands/StockMovements/PostStockMovementCommand.cs", "PostStockMovementCommandHandler", "NormalizeExternalMovementTypeOrReject", 1, 0, "公开 postInventoryMovement facade 的移动类型校验"),
        PostingTarget($"{InventoryWebRoot}/Application/Commands/StockMovements/PostStockMovementCommand.cs", "PostStockMovementCommandHandler", "NormalizeOwnerTypeOrReject", 1, 0, "公开 postInventoryMovement facade 的归属类型校验"),
        PostingTarget($"{InventoryWebRoot}/Application/Commands/StockMovements/PostStockMovementCommand.cs", "PostStockMovementCommandHandler", "NormalizeRequired", 1, 0, "公开 postInventoryMovement facade 的必填校验"),
        PostingExcluded($"{InventoryWebRoot}/Application/IntegrationEventHandlers/InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.cs", "InventoryMovementRequestedIntegrationEventHandlerForPostingMovement", "SendStatusTransferAsync", 1, 0, "状态转移集成事件消费者，无原始 HTTP facade"),
        PostingExcluded($"{InventoryWebRoot}/Application/IntegrationEventHandlers/InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.cs", "InventoryMovementRequestedIntegrationEventHandlerForPostingMovement", "ParseReservationId", 1, 0, "库存移动请求集成事件消费者，无原始 HTTP facade"),
        PostingExcluded($"{InventoryWebRoot}/Application/Commands/StockMovements/InventoryPostingRejectedException.cs", "InventoryPostingRejectedException", "FromDomain", 1, 0, "FromDomain 工厂自身的构造点由行为测试覆盖，不计入公开调用点"),
    ];

    [Fact]
    public void Inventory_transport_visible_known_exceptions_have_a_closed_target_and_exclusion_ledger()
    {
        var documents = LoadDocuments();
        var discovered = InventoryKnownExceptionUserMessageSourceAnalyzer.DiscoverKnownExceptions(documents);
        var expectedKeys = ExpectedKnownExceptionSites.Select(site => site.Key).ToArray();

        Assert.Equal(37, discovered.Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(19, ExpectedKnownExceptionSites
            .Where(site => site.Kind == InventoryKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(18, ExpectedKnownExceptionSites
            .Where(site => site.Kind == InventoryKnownExceptionSiteKind.Excluded)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());

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
        Assert.True(unclassified.Length == 0, string.Join(Environment.NewLine, unclassified));

        foreach (var expected in ExpectedKnownExceptionSites)
        {
            var match = Assert.Single(discovered, site => site.Key == expected.Key);
            Assert.Equal(expected.DirectKnownExceptionCount, match.DirectKnownExceptionCount);
        }

        var violations = InventoryKnownExceptionUserMessageSourceAnalyzer.AnalyzeKnownExceptionMessages(
            documents,
            ExpectedKnownExceptionSites.Where(site => site.Kind == InventoryKnownExceptionSiteKind.Excluded).ToArray());
        Assert.True(
            violations.Count == 0,
            "Inventory transportVisible 用户消息必须是中文、静态可分析、安全且不超过 60 个估算字符。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Inventory_posting_rejection_calls_have_a_closed_target_and_exclusion_ledger()
    {
        var documents = LoadDocuments();
        var discovered = InventoryKnownExceptionUserMessageSourceAnalyzer.DiscoverPostingRejectedCalls(documents);
        var expectedKeys = ExpectedPostingRejectedSites.Select(site => site.Key).ToArray();

        Assert.Equal(17, ExpectedPostingRejectedSites
            .Where(site => site.Kind == InventoryKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectConstructionCount));
        Assert.Equal(3, ExpectedPostingRejectedSites
            .Where(site => site.Kind == InventoryKnownExceptionSiteKind.Target)
            .Sum(site => site.FromDomainCallCount));
        Assert.Equal(3, ExpectedPostingRejectedSites
            .Where(site => site.Kind == InventoryKnownExceptionSiteKind.Excluded)
            .Sum(site => site.DirectConstructionCount));
        Assert.Equal(20, ExpectedPostingRejectedSites
            .Where(site => site.Kind == InventoryKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectConstructionCount + site.FromDomainCallCount));
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());

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
        Assert.True(unclassified.Length == 0, string.Join(Environment.NewLine, unclassified));

        foreach (var expected in ExpectedPostingRejectedSites)
        {
            var match = Assert.Single(discovered, site => site.Key == expected.Key);
            Assert.Equal(expected.DirectConstructionCount, match.DirectConstructionCount);
            Assert.Equal(expected.FromDomainCallCount, match.FromDomainCallCount);
        }

        var violations = InventoryKnownExceptionUserMessageSourceAnalyzer.AnalyzePostingRejectedMessages(
            documents,
            ExpectedPostingRejectedSites.Where(site => site.Kind == InventoryKnownExceptionSiteKind.Excluded).ToArray());
        Assert.True(
            violations.Count == 0,
            "Inventory postInventoryMovement 拒绝消息必须是中文、静态可分析、安全且不超过 60 个估算字符。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void English_user_message_mutation_is_rejected()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"Unable to save\"); } }";

        var violations = InventoryKnownExceptionUserMessageSourceAnalyzer.AnalyzeKnownExceptionMessages(
            [new InventoryKnownExceptionSourceDocument("Probe.cs", source)],
            []);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void Dynamic_known_exception_mutation_is_rejected()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(System.Exception exception) { throw new KnownException(exception.Message, exception); } }";

        var violations = InventoryKnownExceptionUserMessageSourceAnalyzer.AnalyzeKnownExceptionMessages(
            [new InventoryKnownExceptionSourceDocument("Probe.cs", source)],
            []);

        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], violations);
    }

    [Fact]
    public void Known_exception_subclass_mutation_is_rejected()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class ProbeException : KnownException { public ProbeException(string message) : base(message) { } } class Probe { void Handle() { throw new ProbeException(\"Unable to save\"); } }";

        var violations = InventoryKnownExceptionUserMessageSourceAnalyzer.AnalyzeKnownExceptionMessages(
            [new InventoryKnownExceptionSourceDocument("Probe.cs", source)],
            []);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void English_posting_rejection_mutation_is_rejected()
    {
        const string source =
            "using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements; class Probe { void Handle() { throw new InventoryPostingRejectedException(\"POSTING_REJECTED\", \"Unable to post\"); } }";

        var violations = InventoryKnownExceptionUserMessageSourceAnalyzer.AnalyzePostingRejectedMessages(
            [new InventoryKnownExceptionSourceDocument("Probe.cs", source)],
            []);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void Dynamic_posting_rejection_mutation_is_rejected()
    {
        const string source =
            "using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements; class Probe { void Handle(System.Exception exception) { throw new InventoryPostingRejectedException(\"POSTING_REJECTED\", exception.Message, exception); } }";

        var violations = InventoryKnownExceptionUserMessageSourceAnalyzer.AnalyzePostingRejectedMessages(
            [new InventoryKnownExceptionSourceDocument("Probe.cs", source)],
            []);

        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], violations);
    }

    [Theory]
    [InlineData(InventoryDomainFailureReason.NegativeOnHand, InventoryPostingFailureCodes.NegativeOnHand)]
    [InlineData(InventoryDomainFailureReason.IdempotencyConflict, InventoryPostingFailureCodes.IdempotencyConflict)]
    [InlineData(InventoryDomainFailureReason.DimensionMismatch, InventoryPostingFailureCodes.DimensionMismatch)]
    [InlineData(InventoryDomainFailureReason.LedgerFrozen, InventoryPostingFailureCodes.LedgerFrozen)]
    [InlineData(InventoryDomainFailureReason.ReservationAllocationRejected, InventoryPostingFailureCodes.ReservationAllocationRejected)]
    [InlineData(InventoryDomainFailureReason.CommittedStockProtection, InventoryPostingFailureCodes.ReservationAllocationRejected)]
    [InlineData(InventoryDomainFailureReason.PostingRejected, InventoryPostingFailureCodes.PostingRejected)]
    public void From_domain_preserves_failure_code_without_leaking_domain_message(
        InventoryDomainFailureReason reason,
        string expectedFailureCode)
    {
        const string dynamicDomainMessage = "provider failure details must not reach the public response";

        var exception = InventoryPostingRejectedException.FromDomain(
            new InventoryDomainException(reason, dynamicDomainMessage));

        Assert.Equal(expectedFailureCode, exception.FailureCode);
        Assert.NotEqual(dynamicDomainMessage, exception.FailureMessage);
        Assert.Contains("库存", exception.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void From_domain_describes_committed_stock_protection_as_available_quantity_guard()
    {
        var exception = InventoryPostingRejectedException.FromDomain(
            new InventoryDomainException(
                InventoryDomainFailureReason.CommittedStockProtection,
                "Stock movement would breach committed stock protection."));

        Assert.Equal("出库数量超过未预留的可用库存，不能完成过账。", exception.FailureMessage);
    }

    [Fact]
    public void Unknown_domain_failure_reason_fails_closed_instead_of_using_generic_public_mapping()
    {
        var exception = new InventoryDomainException(
            (InventoryDomainFailureReason)999,
            "unknown domain failure");

        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryPostingRejectedException.FromDomain(exception));
    }

    [Fact]
    public void Every_defined_domain_failure_reason_has_an_explicit_public_message_mapping()
    {
        const string genericMessage = "库存过账被拒绝，请核对库存状态后重试。";

        foreach (var reason in Enum.GetValues<InventoryDomainFailureReason>())
        {
            var exception = InventoryPostingRejectedException.FromDomain(
                new InventoryDomainException(reason, "domain details"));

            if (reason == InventoryDomainFailureReason.PostingRejected)
            {
                Assert.Equal(genericMessage, exception.FailureMessage);
            }
            else
            {
                Assert.NotEqual(genericMessage, exception.FailureMessage);
            }
        }
    }

    [Fact]
    public async Task Approval_client_replaces_failed_envelope_message_with_a_stable_public_message()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "{\"data\":null,\"success\":false,\"message\":\"downstream stack trace\",\"code\":500}"))
        {
            BaseAddress = new Uri("http://approval.test"),
        };
        var client = new HttpStockCountApprovalClient(httpClient, new TestInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => client.StartApprovalAsync(
            new StockCountApprovalRequest(
                "org-001",
                "env-dev",
                "APT-WB-CNT-001",
                "inventory",
                "COUNT-VARIANCE",
                "COUNT-001",
                "system:inventory",
                12m),
            CancellationToken.None));

        Assert.Equal("审批服务未返回审批链，请稍后重试。", exception.Message);
        Assert.DoesNotContain("downstream", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static InventoryKnownExceptionSite Target(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        string reason) =>
        new(path, typeName, methodName, directKnownExceptionCount, InventoryKnownExceptionSiteKind.Target, reason);

    private static InventoryKnownExceptionSite Excluded(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        string reason) =>
        new(path, typeName, methodName, directKnownExceptionCount, InventoryKnownExceptionSiteKind.Excluded, reason);

    private static InventoryPostingRejectedSite PostingTarget(
        string path,
        string typeName,
        string methodName,
        int directConstructionCount,
        int fromDomainCallCount,
        string reason) =>
        new(path, typeName, methodName, directConstructionCount, fromDomainCallCount, InventoryKnownExceptionSiteKind.Target, reason);

    private static InventoryPostingRejectedSite PostingExcluded(
        string path,
        string typeName,
        string methodName,
        int directConstructionCount,
        int fromDomainCallCount,
        string reason) =>
        new(path, typeName, methodName, directConstructionCount, fromDomainCallCount, InventoryKnownExceptionSiteKind.Excluded, reason);

    private static IReadOnlyCollection<InventoryKnownExceptionSourceDocument> LoadDocuments()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, InventorySourceRoot.Replace('/', Path.DirectorySeparatorChar));
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(file => new InventoryKnownExceptionSourceDocument(
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

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed record TestInternalServiceTokenProvider(string BearerToken = "test-token") : IInternalServiceTokenProvider;
}
