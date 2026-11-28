using Microsoft.Extensions.Configuration;
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

    private static readonly IReadOnlyCollection<InventoryExcludedMessageSite> KnownExceptionExclusions =
    [
        new($"{InventoryWebRoot}/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs", "QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer", "HandleValidEventAsync", "Quality 集成事件消费者，无原始 HTTP facade"),
        new($"{InventoryWebRoot}/Application/Commands/StockReservations/ReleaseStockReservationCommand.cs", "ReleaseStockReservationCommandHandler", "Handle", "reservation release 为 internal endpoint"),
        new($"{InventoryWebRoot}/Application/Commands/StockCounts/CompleteStockCountAdjustmentApprovalCommand.cs", "CompleteStockCountAdjustmentApprovalCommandHandler", "Handle", "审批回写由集成事件消费者触发，无原始 HTTP facade"),
        new($"{InventoryWebRoot}/Application/Commands/StockReservations/ReserveStockCommand.cs", "ReserveStockCommandHandler", "Handle", "reservation reserve 为 internal endpoint"),
        new($"{InventoryWebRoot}/Application/Commands/StockReservations/ReserveStockCommand.cs", "ReserveFefoStockCommandHandler", "Handle", "reservation FEFO 为 internal endpoint"),
        new($"{InventoryWebRoot}/Application/Commands/StockStatusTransfers/PostStockStatusTransferCommand.cs", "PostStockStatusTransferCommandHandler", "Handle", "质量状态转移为 internal endpoint"),
        new($"{InventoryWebRoot}/Application/Commands/StockReservations/RenewStockReservationCommand.cs", "RenewStockReservationCommandHandler", "Handle", "reservation renew 为 internal endpoint"),
        new($"{InventoryWebRoot}/Application/Commands/StockReservations/MarkStockReservationPickedCommand.cs", "MarkStockReservationPickedCommandHandler", "Handle", "reservation pick 为 internal endpoint"),
    ];

    private static readonly IReadOnlyCollection<InventoryExcludedMessageSite> PostingRejectedExclusions =
    [
        new($"{InventoryWebRoot}/Application/IntegrationEventHandlers/InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.cs", "InventoryMovementRequestedIntegrationEventHandlerForPostingMovement", "SendStatusTransferAsync", "状态转移集成事件消费者，无原始 HTTP facade"),
        new($"{InventoryWebRoot}/Application/IntegrationEventHandlers/InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.cs", "InventoryMovementRequestedIntegrationEventHandlerForPostingMovement", "ParseReservationId", "库存移动请求集成事件消费者，无原始 HTTP facade"),
        new($"{InventoryWebRoot}/Application/Commands/StockMovements/InventoryPostingRejectedException.cs", "InventoryPostingRejectedException", "FromDomain", "FromDomain 工厂自身的构造点由行为测试覆盖，不计入公开调用点"),
        new($"{InventoryWebRoot}/Application/Commands/StockMovements/InventoryPostingRejectedException.cs", "InventoryPostingRejectedException", "ForUnitCostAuthority", "成本权威拒绝由集成事件消费者触发，无原始 HTTP facade"),
    ];

    [Fact]
    public void Inventory_transport_visible_known_exceptions_use_safe_user_messages()
    {
        var documents = LoadDocuments();
        var violations = InventoryKnownExceptionUserMessageSourceAnalyzer.AnalyzeKnownExceptionMessages(
            documents, KnownExceptionExclusions);
        Assert.True(
            violations.Count == 0,
            "Inventory transportVisible 用户消息必须是中文、静态可分析、安全且不超过 60 个估算字符。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Inventory_posting_rejections_use_safe_user_messages()
    {
        var documents = LoadDocuments();
        var violations = InventoryKnownExceptionUserMessageSourceAnalyzer.AnalyzePostingRejectedMessages(
            documents, PostingRejectedExclusions);
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

    [Theory]
    [InlineData(false, "test-token")]
    [InlineData(true, "synthetic-inventory-approval-token")]
    public async Task Approval_client_replaces_failed_envelope_message_with_a_stable_public_message(bool scoped, string expectedBearer)
    {
        using var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "{\"data\":null,\"success\":false,\"message\":\"downstream stack trace\",\"code\":500}");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://approval.test"),
        };
        const string scope = "Approval:CreateChain:OutboundCredentials:0:";
        var configuration = scoped ? new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [scope + "OrganizationId"] = "org-001",
            [scope + "EnvironmentId"] = "env-dev",
            [scope + "BearerToken"] = "synthetic-inventory-approval-token",
        }).Build() : null;
        var client = new HttpStockCountApprovalClient(httpClient, new TestInternalServiceTokenProvider(), configuration);

        var exception = await Assert.ThrowsAsync<KnownException>(() => client.StartApprovalAsync(
            new StockCountApprovalRequest(
                "org-001",
                "env-dev",
                "APT-WB-CNT-001",
                "inventory",
                "COUNT-VARIANCE",
                "COUNT-001",
                "system:inventory",
                12m,
                "adjustment-001"),
            CancellationToken.None));

        Assert.Equal(expectedBearer, handler.BearerToken);
        Assert.Equal("审批服务未返回审批链，请稍后重试。", exception.Message);
        Assert.DoesNotContain("downstream", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<InventoryKnownExceptionSourceDocument> LoadDocuments()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, InventorySourceRoot.Replace('/', Path.DirectorySeparatorChar));
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(sourceRoot, file)
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
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
        public string? BearerToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            BearerToken = request.Headers.Authorization?.Parameter;
            _ = cancellationToken;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed record TestInternalServiceTokenProvider(string BearerToken = "test-token") : IInternalServiceTokenProvider;
}
