using NetCorePal.Extensions.Primitives;

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
    ];

    private static readonly IReadOnlyCollection<ErpKnownExceptionSite> ExpectedSites =
    [
        Target(PurchaseOrderApprovalClientPath, "HttpPurchaseOrderApprovalClient", "StartApprovalAsync", 1, "sync approval facade"),
        Target(CustomerCreditProfileReaderPath, "HttpCustomerCreditProfileReader", "GetAsync", 2, "sync sales-order facade"),
        Target(BusinessPartnerAvailabilityGatePath, "BusinessPartnerAvailabilityGate", "EnsureActiveAsync", 1, "sync sales-order facade"),
        Excluded(WmsOutboundCancellationClientPath, "HttpWmsOutboundCancellationClient", "FindOutboundOrderAsync", 1, "deferred sales-order cancellation; no public facade"),
        Target(AccountPayableSourceDocumentGuardPath, "AccountPayableSourceDocumentGuard", "EnsureSourceDocumentAndSupplierAsync", 2, "sync payable commands"),
        Target(AccountReceivableSourceDocumentGuardPath, "AccountReceivableSourceDocumentGuard", "EnsureSourceDocumentAndCustomerAsync", 2, "sync receivable commands"),
        Target(WorkCenterRateRevisionAllocatorPath, "WorkCenterRateRevisionAllocator", "AllocateAsync", 1, "shared helper reaches sync work-center rate facades"),

        Target(ErpFinanceCommandsPath, "AccountingPeriodPostingGuard", "FindPeriodAsync", 1, "shared helper reaches sync finance facades"),
        Target(ErpFinanceCommandsPath, "AccountingPeriodPostingGuard", "EnsureOpenAsync", 1, "shared helper reaches sync finance facades"),
        Target(ErpFinanceCommandsPath, "ExecutePaymentExecutionCommandHandler", "Handle", 2, "sync payment-execution facade"),
        Excluded(ErpFinanceCommandsPath, "RegisterAccountReceivableCollectionCommandHandler", "Handle", 1, "deferred receivable collection; no public facade"),
        Target(ErpFinanceCommandsPath, "RegisterCashReceiptCommandHandler", "Handle", 1, "sync cash-receipt facade"),
        Target(ErpFinanceCommandsPath, "MatchCashReceiptCommandHandler", "Handle", 3, "sync cash-receipt facade"),
        Target(ErpFinanceCommandsPath, "PaymentExecutionCommandFacts", "LoadPayableVoucherAllocationsAsync", 2, "sync payment-execution facade"),
        Target(ErpFinanceCommandsPath, "PaymentExecutionCommandFacts", "ResolveSingleSupplierCode", 1, "sync payment-execution facade"),

        Target(ErpProcurementCommandsPath, "ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler", "Handle", 5, "sync requisition conversion facade"),
        Target(ErpProcurementCommandsPath, "RecordPurchaseReceiptCommandHandler", "Handle", 2, "sync purchase-receipt facade"),
        Excluded(ErpProcurementCommandsPath, "RecordSupplierInvoiceCommandHandler", "Handle", 2, "deferred supplier-invoice creation; no public facade"),
        Excluded(ErpProcurementCommandsPath, "ReleaseSupplierInvoicePaymentHoldCommandHandler", "Handle", 3, "deferred supplier-invoice payment hold; no public facade"),
        Excluded(ErpProcurementCommandsPath, "VoidSupplierInvoicePaymentHoldCommandHandler", "Handle", 2, "deferred supplier-invoice payment hold; no public facade"),
        Excluded(ErpProcurementCommandsPath, "RequestPurchaseOrderChangeCommandHandler", "Handle", 2, "deferred purchase-order change; no public facade"),
        Excluded(ErpProcurementCommandsPath, "ClosePurchaseOrderLineCommandHandler", "Handle", 2, "deferred purchase-order close; no public facade"),
        Excluded(ErpProcurementCommandsPath, "CancelPurchaseOrderCommandHandler", "Handle", 5, "deferred purchase-order cancellation; no public facade"),

        Target(ErpSalesCommandsPath, "ApproveQuotationCommandHandler", "Handle", 1, "sync quotation facade"),
        Target(ErpSalesCommandsPath, "CreateSalesOrderCommandHandler", "Handle", 3, "sync sales-order facade"),
        Target(ErpSalesCommandsPath, "CreateSalesOrderCommandHandler", "ReuseExistingOrderAsync", 1, "sync sales-order facade"),
        Target(ErpSalesCommandsPath, "ReleaseSalesOrderCreditHoldCommandHandler", "Handle", 2, "sync credit-hold facade"),
        Target(ErpSalesCommandsPath, "ReleaseDeliveryOrderCommandHandler", "Handle", 3, "sync delivery facade"),
        Excluded(ErpSalesCommandsPath, "ChangeSalesOrderLineCommandHandler", "Handle", 2, "deferred sales-order change; no public facade"),
        Excluded(ErpSalesCommandsPath, "CancelSalesOrderCommandHandler", "Handle", 4, "deferred sales-order cancellation; no public facade"),
        Excluded(ErpSalesCommandsPath, "CreateSalesReturnAuthorizationCommandHandler", "Handle", 6, "deferred sales-return authorization; no public facade"),

        Target(ErpSalesFinanceQueriesPath, "GetAccountPayableBySourceDocumentQueryHandler", "Handle", 1, "sync payable query facade"),
        Target(ErpSalesFinanceQueriesPath, "GetAccountReceivableBySourceDocumentQueryHandler", "Handle", 1, "sync receivable query facade"),
        Target(ErpSalesFinanceQueriesPath, "GetCostCandidateBySourceDocumentQueryHandler", "Handle", 1, "sync cost-candidate query facade"),

        Excluded(ApprovalCompletedHandlerPath, "ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder", "HandleValidEventAsync", 2, "async integration-event consumer; no public facade"),
        Excluded(ErpReturnHandlersPath, "WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn", "HandleValidEventCoreAsync", 5, "async integration-event consumer; no public facade"),
        Excluded(ErpReturnHandlersPath, "QualityInspectionResultIntegrationEventHandlerForSettleSalesReturnCredit", "HandleValidEventAsync", 3, "async integration-event consumer; no public facade"),
    ];

    private static readonly IReadOnlyDictionary<string, int> DynamicTargetSiteCounts = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [$"{PurchaseOrderApprovalClientPath}|HttpPurchaseOrderApprovalClient|StartApprovalAsync"] = 1,
        [$"{CustomerCreditProfileReaderPath}|HttpCustomerCreditProfileReader|GetAsync"] = 2,
        [$"{ErpProcurementCommandsPath}|RecordPurchaseReceiptCommandHandler|Handle"] = 1,
        [$"{ErpSalesCommandsPath}|CreateSalesOrderCommandHandler|Handle"] = 1,
        [$"{ErpSalesCommandsPath}|ReleaseSalesOrderCreditHoldCommandHandler|Handle"] = 1,
        [$"{ErpSalesCommandsPath}|ReleaseDeliveryOrderCommandHandler|Handle"] = 1,
    };

    [Fact]
    public void Erp_sync_known_exception_sites_have_a_closed_target_and_exclusion_ledger()
    {
        var documents = LoadDocuments();
        var discovered = ErpKnownExceptionUserMessageSourceAnalyzer.Discover(documents);
        var expectedKeys = ExpectedSites.Select(site => site.Key).ToArray();

        Assert.Equal(SourcePaths.Count, documents.Count);
        Assert.All(documents, document => Assert.False(string.IsNullOrWhiteSpace(document.Text), $"Erp 源文件缺失或为空：{document.Path}"));
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(80, discovered.Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(40, ExpectedSites
            .Where(site => site.Kind == ErpKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(40, ExpectedSites
            .Where(site => site.Kind == ErpKnownExceptionSiteKind.Excluded)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(7, DynamicTargetSiteCounts.Values.Sum());
        Assert.Equal(
            33,
            ExpectedSites
                .Where(site => site.Kind == ErpKnownExceptionSiteKind.Target)
                .Sum(site => site.DirectKnownExceptionCount) - DynamicTargetSiteCounts.Values.Sum());
        Assert.All(
            DynamicTargetSiteCounts,
            dynamicSite => Assert.Contains(
                ExpectedSites,
                site => site.Kind == ErpKnownExceptionSiteKind.Target && site.Key == dynamicSite.Key));

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

        foreach (var expected in ExpectedSites)
        {
            var match = Assert.Single(discovered, site => site.Key == expected.Key);
            Assert.Equal(expected.DirectKnownExceptionCount, match.DirectKnownExceptionCount);
        }

        var targetSites = ExpectedSites.Where(site => site.Kind == ErpKnownExceptionSiteKind.Excluded).ToArray();
        var violations = ErpKnownExceptionUserMessageSourceAnalyzer.AnalyzeUserMessages(documents, targetSites);

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

        var violations = ErpKnownExceptionUserMessageSourceAnalyzer.AnalyzeUserMessages(
            [new ErpKnownExceptionSourceDocument("Probe.cs", source)],
            []);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void Dynamic_passthrough_mutation_is_rejected()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(System.Exception exception) { throw new KnownException(exception.Message, exception); } }";

        var violations = ErpKnownExceptionUserMessageSourceAnalyzer.AnalyzeUserMessages(
            [new ErpKnownExceptionSourceDocument("Probe.cs", source)],
            []);

        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], violations);
    }

    private static ErpKnownExceptionSite Target(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        string reason) =>
        new(path, typeName, methodName, directKnownExceptionCount, ErpKnownExceptionSiteKind.Target, reason);

    private static ErpKnownExceptionSite Excluded(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        string reason) =>
        new(path, typeName, methodName, directKnownExceptionCount, ErpKnownExceptionSiteKind.Excluded, reason);

    private static IReadOnlyCollection<ErpKnownExceptionSourceDocument> LoadDocuments()
    {
        var repositoryRoot = FindRepositoryRoot();
        return SourcePaths
            .Select(path => Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)))
            .Select(file => new ErpKnownExceptionSourceDocument(
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
