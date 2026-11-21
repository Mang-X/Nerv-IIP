using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpKnownExceptionMessageArchitectureTests
{
    private const string PurchaseOrderApprovalClientPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Approval/PurchaseOrderApprovalClient.cs";
    private const string CustomerCreditProfileReaderPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/MasterData/CustomerCreditProfileReader.cs";
    private const string BusinessPartnerAvailabilityGatePath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/MasterData/BusinessPartnerAvailabilityGate.cs";
    private const string WmsOutboundCancellationClientPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Wms/WmsOutboundCancellationClient.cs";
    private const string AccountPayableSourceDocumentGuardPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/AccountPayableSourceDocumentGuard.cs";
    private const string AccountReceivableSourceDocumentGuardPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/AccountReceivableSourceDocumentGuard.cs";
    private const string ConfigureWorkCenterCostRateCommandPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/ConfigureWorkCenterCostRateCommand.cs";
    private const string WorkCenterRateRevisionAllocatorPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/WorkCenterRateRevisionAllocator.cs";
    private const string ErpFinanceCommandsPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/ErpFinanceCommands.cs";
    private const string ErpProcurementCommandsPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/ErpProcurementCommands.cs";
    private const string ErpSalesCommandsPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Sales/ErpSalesCommands.cs";
    private const string ErpSalesFinanceQueriesPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/SalesFinance/ErpSalesFinanceQueries.cs";
    private const string ApprovalCompletedHandlerPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.cs";
    private const string ErpReturnHandlersPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/ErpReturnIntegrationEventHandlers.cs";

    private const string ErpCodingIdempotencyKeyPolicyPath =
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Validation/ErpCodingIdempotencyKeyPolicy.cs";

    private static readonly IReadOnlyCollection<string> SourcePaths =
    [
        PurchaseOrderApprovalClientPath,
        CustomerCreditProfileReaderPath,
        BusinessPartnerAvailabilityGatePath,
        WmsOutboundCancellationClientPath,
        AccountPayableSourceDocumentGuardPath,
        AccountReceivableSourceDocumentGuardPath,
        ConfigureWorkCenterCostRateCommandPath,
        WorkCenterRateRevisionAllocatorPath,
        ErpFinanceCommandsPath,
        ErpProcurementCommandsPath,
        ErpSalesCommandsPath,
        ErpSalesFinanceQueriesPath,
        ApprovalCompletedHandlerPath,
        ErpReturnHandlersPath,
        ErpCodingIdempotencyKeyPolicyPath,
    ];

    private static readonly IReadOnlyCollection<ExcludedUserMessageSite> ExcludedSites =
    [
        new(WmsOutboundCancellationClientPath, "HttpWmsOutboundCancellationClient", "FindOutboundOrderAsync", "deferred sales-order cancellation; no public facade"),
        new(ErpFinanceCommandsPath, "RegisterAccountReceivableCollectionCommandHandler", "Handle", "deferred receivable collection; no public facade"),
        new(ErpProcurementCommandsPath, "RecordSupplierInvoiceCommandHandler", "Handle", "deferred supplier-invoice creation; no public facade"),
        new(ErpProcurementCommandsPath, "ReleaseSupplierInvoicePaymentHoldCommandHandler", "Handle", "deferred supplier-invoice payment hold; no public facade"),
        new(ErpProcurementCommandsPath, "VoidSupplierInvoicePaymentHoldCommandHandler", "Handle", "deferred supplier-invoice payment hold; no public facade"),
        new(ErpProcurementCommandsPath, "RequestPurchaseOrderChangeCommandHandler", "Handle", "deferred purchase-order change; no public facade"),
        new(ErpProcurementCommandsPath, "ReschedulePurchaseOrderLineCommandHandler", "Handle", "internal purchase-order line reschedule; no public facade"),
        new(ErpProcurementCommandsPath, "CancelPurchaseOrderLineCommandHandler", "Handle", "internal purchase-order line cancellation; no public facade"),
        new(ErpProcurementCommandsPath, "ClosePurchaseOrderLineCommandHandler", "Handle", "deferred purchase-order close; no public facade"),
        new(ErpProcurementCommandsPath, "CancelPurchaseOrderCommandHandler", "Handle", "deferred purchase-order cancellation; no public facade"),
        new(ErpSalesCommandsPath, "ChangeSalesOrderLineCommandHandler", "Handle", "deferred sales-order change; no public facade"),
        new(ErpSalesCommandsPath, "CancelSalesOrderCommandHandler", "Handle", "deferred sales-order cancellation; no public facade"),
        new(ErpSalesCommandsPath, "CreateSalesReturnAuthorizationCommandHandler", "Handle", "deferred sales-return authorization; no public facade"),
        new(ApprovalCompletedHandlerPath, "ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder", "HandleValidEventAsync", "async integration-event consumer; no public facade"),
        new(ErpReturnHandlersPath, "WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn", "HandleValidEventCoreAsync", "async integration-event consumer; no public facade"),
        new(ErpReturnHandlersPath, "QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit", "HandleValidEventAsync", "async integration-event consumer; no public facade"),
    ];

    [Fact]
    public void Erp_sync_known_exception_messages_are_safe_for_transport()
    {
        var documents = LoadDocuments();
        Assert.All(documents, document => Assert.False(string.IsNullOrWhiteSpace(document.Text), $"Erp 源文件缺失或为空：{document.Path}"));
        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            documents, ExcludedSites, UserMessagePolicy.RequiredSafeText);

        Assert.True(
            violations.Count == 0,
            "Erp 同步公开 KnownException 用户消息必须是静态中文、安全且不超过 60 个估算字符。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void English_user_message_mutation_is_rejected()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"Unable to save\"); } }";

        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument("Probe.cs", source)],
            []);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void Dynamic_passthrough_mutation_is_rejected()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(System.Exception exception) { throw new KnownException(exception.Message, exception); } }";

        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument("Probe.cs", source)],
            []);

        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], violations);
    }

    private static IReadOnlyCollection<UserMessageSourceDocument> LoadDocuments()
    {
        var repositoryRoot = FindRepositoryRoot();
        return SourcePaths
            .Select(path => Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)))
            .Select(file => new UserMessageSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.Exists(file) ? File.ReadAllText(file) : string.Empty))
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
