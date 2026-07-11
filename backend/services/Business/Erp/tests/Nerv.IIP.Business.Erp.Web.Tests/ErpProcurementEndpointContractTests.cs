using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Approval;
using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Wms;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpProcurementEndpointContractTests
{
    [Fact]
    public void Erp_procurement_endpoints_expose_issue_137_routes_permissions_policies_and_operation_ids()
    {
        var contracts = ErpProcurementEndpointContracts.All.ToArray();

        Assert.Equal(16, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/purchase-requisitions/from-suggestion"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createErpPurchaseRequisitionFromSuggestion");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/erp/purchase-requisitions"
            && x.PermissionCode == ErpPermissionCodes.ProcurementRead
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listErpPurchaseRequisitions");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/purchase-requisitions/convert-to-purchase-order"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "convertErpPurchaseRequisitionsToPurchaseOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/rfqs"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createErpRequestForQuotation");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/supplier-quotations"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "receiveErpSupplierQuotation");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/purchase-orders"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createErpPurchaseOrder");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/changes" && x.OperationId == "requestErpPurchaseOrderChange");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/lines/{lineNo}/final-delivery" && x.OperationId == "closeErpPurchaseOrderLineFinalDelivery");
        Assert.Contains(contracts, x => x.Route == "/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/cancel" && x.OperationId == "cancelErpPurchaseOrder");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/purchase-receipts"
            && x.PermissionCode == ErpPermissionCodes.ProcurementManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "recordErpPurchaseReceipt");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/erp/purchase-receipts/{purchaseReceiptNo}/source-document"
            && x.PermissionCode == ErpPermissionCodes.ProcurementRead
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "getErpPurchaseReceiptSourceDocument");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/supplier-invoices"
            && x.PermissionCode == ErpPermissionCodes.FinanceManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "recordErpSupplierInvoice");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/supplier-invoices/{invoiceNo}/release-payment-hold"
            && x.PermissionCode == ErpPermissionCodes.FinanceManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "releaseErpSupplierInvoicePaymentHold");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/erp/supplier-invoices/{invoiceNo}/void-payment-hold"
            && x.PermissionCode == ErpPermissionCodes.FinanceManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "voidErpSupplierInvoicePaymentHold");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/erp/rfqs"
            && x.PermissionCode == ErpPermissionCodes.ProcurementRead
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listErpRequestsForQuotation");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/erp/purchase-orders"
            && x.PermissionCode == ErpPermissionCodes.ProcurementRead
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listErpPurchaseOrders");
    }

    [Theory]
    [InlineData(typeof(CreatePurchaseRequisitionFromSuggestionEndpoint))]
    [InlineData(typeof(ListPurchaseRequisitionsEndpoint))]
    [InlineData(typeof(ConvertPurchaseRequisitionsToPurchaseOrderEndpoint))]
    [InlineData(typeof(CreateRequestForQuotationEndpoint))]
    [InlineData(typeof(ReceiveSupplierQuotationEndpoint))]
    [InlineData(typeof(ListRequestsForQuotationEndpoint))]
    [InlineData(typeof(CreatePurchaseOrderEndpoint))]
    [InlineData(typeof(RecordPurchaseReceiptEndpoint))]
    [InlineData(typeof(GetPurchaseReceiptSourceDocumentEndpoint))]
    [InlineData(typeof(RecordSupplierInvoiceEndpoint))]
    [InlineData(typeof(ReleaseSupplierInvoicePaymentHoldEndpoint))]
    [InlineData(typeof(VoidSupplierInvoicePaymentHoldEndpoint))]
    [InlineData(typeof(ListPurchaseOrdersEndpoint))]
    public void Erp_procurement_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task List_purchase_requisitions_query_applies_status_keyword_and_server_paging()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreatePurchaseRequisitionFromSuggestionCommandHandler(dbContext);
        await handler.Handle(new CreatePurchaseRequisitionFromSuggestionCommand(
            "org-001",
            "env-dev",
            "PR-001",
            "suggestion-001",
            "SKU-RM-1000",
            "kg",
            "SITE-01",
            3m,
            new DateOnly(2026, 6, 5)), CancellationToken.None);
        await handler.Handle(new CreatePurchaseRequisitionFromSuggestionCommand(
            "org-001",
            "env-dev",
            "PR-002",
            "suggestion-002",
            "SKU-RM-2000",
            "kg",
            "SITE-02",
            4m,
            new DateOnly(2026, 6, 6)), CancellationToken.None);
        await handler.Handle(new CreatePurchaseRequisitionFromSuggestionCommand(
            "org-other",
            "env-dev",
            "PR-003",
            "suggestion-003",
            "SKU-RM-2000",
            "kg",
            "SITE-02",
            5m,
            new DateOnly(2026, 6, 7)), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListPurchaseRequisitionsQueryHandler(dbContext).Handle(
            new ListPurchaseRequisitionsQuery("org-001", "env-dev", "Open", "PR-002", 0, 1),
            CancellationToken.None);
        var unknownStatus = await new ListPurchaseRequisitionsQueryHandler(dbContext).Handle(
            new ListPurchaseRequisitionsQuery("org-001", "env-dev", "not-a-status", null, 0, 100),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("PR-002", item.RequisitionNo);
        Assert.Equal("suggestion-002", item.SuggestionId);
        Assert.Equal("Open", item.Status);
        Assert.Equal("SKU-RM-2000", item.SkuCode);
        Assert.Equal(0, unknownStatus.Total);
        Assert.Empty(unknownStatus.Items);
    }

    [Fact]
    public async Task List_requests_for_quotation_query_applies_status_keyword_and_server_paging()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateRequestForQuotationCommandHandler(dbContext);
        await handler.Handle(new CreateRequestForQuotationCommand(
            "org-001",
            "env-dev",
            "RFQ-001",
            ["SUP-001"],
            [new RfqCommandLine("LINE-001", "SKU-RM-1000", "kg", 3m, "SITE-01", new DateOnly(2026, 6, 5))]), CancellationToken.None);
        await handler.Handle(new CreateRequestForQuotationCommand(
            "org-001",
            "env-dev",
            "RFQ-002",
            ["SUP-002", "SUP-003"],
            [new RfqCommandLine("LINE-001", "SKU-RM-2000", "kg", 4m, "SITE-02", new DateOnly(2026, 6, 6))]), CancellationToken.None);
        await handler.Handle(new CreateRequestForQuotationCommand(
            "org-other",
            "env-dev",
            "RFQ-003",
            ["SUP-002"],
            [new RfqCommandLine("LINE-001", "SKU-RM-3000", "kg", 5m, "SITE-02", new DateOnly(2026, 6, 7))]), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListRequestsForQuotationQueryHandler(dbContext).Handle(
            new ListRequestsForQuotationQuery("org-001", "env-dev", "Open", "SUP-002", 0, 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("RFQ-002", item.RfqNo);
        Assert.Equal("Open", item.Status);
        Assert.Contains("SUP-003", item.SupplierCodes);
        Assert.Equal("SKU-RM-2000", Assert.Single(item.Lines).SkuCode);
    }

    [Fact]
    public async Task List_requests_for_quotation_query_rejects_unknown_status_and_caps_take()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateRequestForQuotationCommandHandler(dbContext);
        for (var index = 1; index <= 501; index++)
        {
            await handler.Handle(new CreateRequestForQuotationCommand(
                "org-001",
                "env-dev",
                $"RFQ-{index:D3}",
                ["SUP-001"],
                [new RfqCommandLine("LINE-001", $"SKU-RM-{index:D3}", "kg", 1m, "SITE-01", new DateOnly(2026, 6, 5))]),
                CancellationToken.None);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var unknownStatus = await new ListRequestsForQuotationQueryHandler(dbContext).Handle(
            new ListRequestsForQuotationQuery("org-001", "env-dev", "not-a-status", null, 0, 100),
            CancellationToken.None);
        var capped = await new ListRequestsForQuotationQueryHandler(dbContext).Handle(
            new ListRequestsForQuotationQuery("org-001", "env-dev", null, null, 0, 1000),
            CancellationToken.None);

        Assert.Equal(0, unknownStatus.Total);
        Assert.Empty(unknownStatus.Items);
        Assert.Equal(501, capped.Total);
        Assert.Equal(500, capped.Items.Count);
    }

    [Fact]
    public async Task List_requests_for_quotation_query_applies_skip_offset()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateRequestForQuotationCommandHandler(dbContext);
        await handler.Handle(new CreateRequestForQuotationCommand(
            "org-001",
            "env-dev",
            "RFQ-001",
            ["SUP-001"],
            [new RfqCommandLine("LINE-001", "SKU-RM-001", "kg", 1m, "SITE-01", new DateOnly(2026, 6, 5))]), CancellationToken.None);
        await handler.Handle(new CreateRequestForQuotationCommand(
            "org-001",
            "env-dev",
            "RFQ-002",
            ["SUP-001"],
            [new RfqCommandLine("LINE-001", "SKU-RM-002", "kg", 1m, "SITE-01", new DateOnly(2026, 6, 5))]), CancellationToken.None);
        await handler.Handle(new CreateRequestForQuotationCommand(
            "org-001",
            "env-dev",
            "RFQ-003",
            ["SUP-001"],
            [new RfqCommandLine("LINE-001", "SKU-RM-003", "kg", 1m, "SITE-01", new DateOnly(2026, 6, 5))]), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        SetRfqCreatedAt(dbContext, "RFQ-001", new DateTime(2026, 6, 1, 0, 0, 1, DateTimeKind.Utc));
        SetRfqCreatedAt(dbContext, "RFQ-002", new DateTime(2026, 6, 1, 0, 0, 2, DateTimeKind.Utc));
        SetRfqCreatedAt(dbContext, "RFQ-003", new DateTime(2026, 6, 1, 0, 0, 3, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListRequestsForQuotationQueryHandler(dbContext).Handle(
            new ListRequestsForQuotationQuery("org-001", "env-dev", null, null, 1, 1),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        Assert.Equal("RFQ-002", Assert.Single(response.Items).RfqNo);
    }

    [Fact]
    public async Task List_purchase_orders_query_returns_procurement_projection()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreatePurchaseOrderCommandHandler(dbContext);
        await handler.Handle(new CreatePurchaseOrderCommand(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListPurchaseOrdersQueryHandler(dbContext).Handle(
            new ListPurchaseOrdersQuery("org-001", "env-dev"), CancellationToken.None);

        Assert.Single(response.Items);
        Assert.Equal("PO-001", response.Items.Single().PurchaseOrderNo);
        Assert.Equal(36m, response.Items.Single().TotalAmount);
    }

    [Fact]
    public async Task Get_purchase_receipt_source_document_query_returns_org_scoped_line_facts()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = Domain.AggregatesModel.PurchaseOrderAggregate.PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-RECEIPT-FACTS",
            "SUP-001",
            "SITE-01",
            [
                new Domain.AggregatesModel.PurchaseOrderAggregate.PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 5m, 10m, new DateOnly(2026, 6, 5)),
                new Domain.AggregatesModel.PurchaseOrderAggregate.PurchaseOrderLineDraft("LINE-002", "SKU-RM-2000", "kg", 7m, 8m, new DateOnly(2026, 6, 5)),
            ]);
        order.MarkApprovalRequested("approval-receipt-facts");
        order.ReleaseAfterApproval("approval-receipt-facts");
        dbContext.PurchaseOrders.Add(order);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordPurchaseReceiptCommandHandler(dbContext).Handle(
            new RecordPurchaseReceiptCommand(
                "org-001",
                "env-dev",
                "RCV-FACTS",
                "PO-RECEIPT-FACTS",
                [
                    new PurchaseReceiptCommandLine("LINE-001", 2m, "inspection", "IQC-STAGE", "LOT-001"),
                    new PurchaseReceiptCommandLine("LINE-002", 3m, "accepted", "IQC-STAGE", "LOT-002"),
                ]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetPurchaseReceiptSourceDocumentQueryHandler(dbContext).Handle(
            new GetPurchaseReceiptSourceDocumentQuery("org-001", "env-dev", "RCV-FACTS"),
            CancellationToken.None);
        var wrongOrg = await new GetPurchaseReceiptSourceDocumentQueryHandler(dbContext).Handle(
            new GetPurchaseReceiptSourceDocumentQuery("org-other", "env-dev", "RCV-FACTS"),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("recorded", response.Status);
        Assert.Equal("RCV-FACTS", response.PurchaseReceiptNo);
        Assert.Null(wrongOrg);
        Assert.Equal(2, response.Lines.Count);
        Assert.Contains(response.Lines, line =>
            line.LineNo == "LINE-001"
            && line.SkuCode == "SKU-RM-1000"
            && line.UomCode == "kg"
            && line.ReceivedQuantity == 2m
            && line.LotNo == "LOT-001"
            && line.Status == "inspection");
        Assert.Contains(response.Lines, line =>
            line.LineNo == "LINE-002"
            && line.SkuCode == "SKU-RM-2000"
            && line.ReceivedQuantity == 3m
            && line.LotNo == "LOT-002");
    }

    [Fact]
    public async Task List_purchase_orders_query_applies_status_keyword_and_server_paging()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreatePurchaseOrderCommandHandler(dbContext);
        await handler.Handle(new CreatePurchaseOrderCommand(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]), CancellationToken.None);
        await handler.Handle(new CreatePurchaseOrderCommand(
            "org-001",
            "env-dev",
            "PO-002",
            "SUP-002",
            "SITE-02",
            [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-2000", "kg", 4m, 10m, new DateOnly(2026, 6, 6))]), CancellationToken.None);
        await handler.Handle(new CreatePurchaseOrderCommand(
            "org-other",
            "env-dev",
            "PO-003",
            "SUP-002",
            "SITE-02",
            [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-3000", "kg", 5m, 10m, new DateOnly(2026, 6, 7))]), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListPurchaseOrdersQueryHandler(dbContext).Handle(
            new ListPurchaseOrdersQuery("org-001", "env-dev", "PendingApproval", "SUP-002", 0, 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("PO-002", item.PurchaseOrderNo);
        Assert.Equal("PendingApproval", item.Status);
    }

    [Fact]
    public async Task List_purchase_orders_query_rejects_unknown_status_and_caps_take()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreatePurchaseOrderCommandHandler(dbContext);
        for (var index = 1; index <= 501; index++)
        {
            await handler.Handle(new CreatePurchaseOrderCommand(
                "org-001",
                "env-dev",
                $"PO-{index:D3}",
                "SUP-001",
                "SITE-01",
                [new PurchaseOrderCommandLine("LINE-001", $"SKU-RM-{index:D3}", "kg", 1m, 10m, new DateOnly(2026, 6, 5))]),
                CancellationToken.None);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var unknownStatus = await new ListPurchaseOrdersQueryHandler(dbContext).Handle(
            new ListPurchaseOrdersQuery("org-001", "env-dev", "not-a-status", null, 0, 100),
            CancellationToken.None);
        var capped = await new ListPurchaseOrdersQueryHandler(dbContext).Handle(
            new ListPurchaseOrdersQuery("org-001", "env-dev", null, null, 0, 1000),
            CancellationToken.None);

        Assert.Equal(0, unknownStatus.Total);
        Assert.Empty(unknownStatus.Items);
        Assert.Equal(501, capped.Total);
        Assert.Equal(500, capped.Items.Count);
    }

    [Fact]
    public async Task Purchase_order_command_generates_number_and_replays_idempotent_create()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var numbering = new ErpCodingService();
        var handler = new CreatePurchaseOrderCommandHandler(dbContext, numbering);
        var command = new CreatePurchaseOrderCommand(
            "org-001",
            "env-dev",
            null,
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))],
            "purchase-order-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first, second);
        var order = Assert.Single(dbContext.PurchaseOrders);
        Assert.Matches("^PO-[0-9]{8}-[0-9]{6}$", order.PurchaseOrderNo);
    }

    [Fact]
    public async Task Convert_purchase_requisitions_uses_supplier_quotation_merges_lines_and_marks_sources_converted()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var approvalClient = new CapturingPurchaseOrderApprovalClient();
        await SeedPurchaseRequisitionAsync(dbContext, "PR-001", "suggestion-001", "SKU-RM-1000", 3m);
        await SeedPurchaseRequisitionAsync(dbContext, "PR-002", "suggestion-002", "SKU-RM-1000", 4m);
        await new ReceiveSupplierQuotationCommandHandler(dbContext).Handle(
            new ReceiveSupplierQuotationCommand(
                "org-001",
                "env-dev",
                "SQ-001",
                "RFQ-001",
                "SUP-001",
                [new SupplierQuotationCommandLine("LINE-001", "SKU-RM-1000", "kg", 10m, 12m, new DateOnly(2026, 6, 7))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler(dbContext, approvalClient: approvalClient).Handle(
            new ConvertPurchaseRequisitionsToPurchaseOrderCommand(
                "org-001",
                "env-dev",
                ["PR-001", "PR-002"],
                PurchaseOrderNo: "PO-REQ-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(PurchaseRequisitionConversionStatus.PurchaseOrderCreated, result.Status);
        Assert.Equal("PO-REQ-001", result.PurchaseOrderNo);
        Assert.Equal("SUP-001", result.SupplierCode);
        Assert.NotNull(approvalClient.LastRequest);
        var order = Assert.Single(dbContext.PurchaseOrders.Include(x => x.Lines).ThenInclude(x => x.SourceLinks));
        var line = Assert.Single(order.Lines);
        Assert.Equal(7m, line.OrderedQuantity);
        Assert.Equal("kg", line.UomCode);
        Assert.Equal(12m, line.UnitPrice);
        Assert.Equal("PR-001", line.SourceLinks.OrderBy(x => x.PurchaseRequisitionNo, StringComparer.Ordinal).First().PurchaseRequisitionNo);
        Assert.Equal(7m, line.SourceLinks.Sum(x => x.Quantity));
        Assert.All(dbContext.PurchaseRequisitions, requisition =>
        {
            Assert.Equal(PurchaseRequisitionStatus.Converted, requisition.Status);
            Assert.Equal("PO-REQ-001", requisition.ConvertedPurchaseOrderNo);
        });
    }

    [Fact]
    public async Task Convert_purchase_requisitions_is_idempotent_after_sources_are_converted()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedPurchaseRequisitionAsync(dbContext, "PR-001", "suggestion-001", "SKU-RM-1000", 3m);
        await new ReceiveSupplierQuotationCommandHandler(dbContext).Handle(
            new ReceiveSupplierQuotationCommand(
                "org-001",
                "env-dev",
                "SQ-001",
                "RFQ-001",
                "SUP-001",
                [new SupplierQuotationCommandLine("LINE-001", "SKU-RM-1000", "kg", 10m, 12m, new DateOnly(2026, 6, 7))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler(dbContext);
        var command = new ConvertPurchaseRequisitionsToPurchaseOrderCommand("org-001", "env-dev", ["PR-001"], PurchaseOrderNo: "PO-REQ-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.PurchaseOrderNo, second.PurchaseOrderNo);
        Assert.Equal(PurchaseRequisitionConversionStatus.AlreadyConverted, second.Status);
        Assert.Single(dbContext.PurchaseOrders);
    }

    [Fact]
    public async Task Convert_purchase_requisitions_uses_stable_business_idempotency_when_random_request_keys_differ()
    {
        await using var provider = CreateInMemoryProvider();
        await using var seedScope = provider.CreateAsyncScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedPurchaseRequisitionAsync(seedContext, "PR-001", "suggestion-001", "SKU-RM-1000", 3m);
        await new ReceiveSupplierQuotationCommandHandler(seedContext).Handle(
            new ReceiveSupplierQuotationCommand(
                "org-001",
                "env-dev",
                "SQ-001",
                "RFQ-001",
                "SUP-001",
                [new SupplierQuotationCommandLine("LINE-001", "SKU-RM-1000", "kg", 10m, 12m, new DateOnly(2026, 6, 7))]),
            CancellationToken.None);
        await seedContext.SaveChangesAsync(CancellationToken.None);
        var coding = new ErpCodingService();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstHandler = new ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler(
            firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            coding);
        var secondHandler = new ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler(
            secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            coding);

        var first = await firstHandler.Handle(
            new ConvertPurchaseRequisitionsToPurchaseOrderCommand("org-001", "env-dev", ["PR-001"], IdempotencyKey: "browser-random-1"),
            CancellationToken.None);
        var second = await secondHandler.Handle(
            new ConvertPurchaseRequisitionsToPurchaseOrderCommand("org-001", "env-dev", ["PR-001"], IdempotencyKey: "browser-random-2"),
            CancellationToken.None);

        Assert.Equal(PurchaseRequisitionConversionStatus.PurchaseOrderCreated, first.Status);
        Assert.Equal(PurchaseRequisitionConversionStatus.PurchaseOrderCreated, second.Status);
        Assert.Equal(first.PurchaseOrderNo, second.PurchaseOrderNo);
    }

    [Fact]
    public async Task Convert_purchase_requisitions_clamps_historical_purchase_order_promised_date_to_required_date()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedPurchaseRequisitionAsync(dbContext, "PR-001", "suggestion-001", "SKU-RM-1000", 3m);
        await new CreatePurchaseOrderCommandHandler(dbContext).Handle(
            new CreatePurchaseOrderCommand(
                "org-001",
                "env-dev",
                "PO-HISTORY-001",
                "SUP-001",
                "SITE-01",
                [new PurchaseOrderCommandLine("LINE-001", "SKU-RM-1000", "kg", 5m, 9m, new DateOnly(2026, 5, 1))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler(dbContext).Handle(
            new ConvertPurchaseRequisitionsToPurchaseOrderCommand("org-001", "env-dev", ["PR-001"], PurchaseOrderNo: "PO-REQ-001"),
            CancellationToken.None);

        var line = Assert.Single(result.Lines!);
        Assert.Equal(new DateOnly(2026, 6, 5), line.PromisedDate);
    }

    [Fact]
    public async Task Purchase_order_changes_use_distinct_approval_chains_before_change_history_is_persisted()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-CHANGE-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]);
        order.MarkApprovalRequested("approval-create-001");
        order.ReleaseAfterApproval("approval-create-001");
        dbContext.PurchaseOrders.Add(order);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RequestPurchaseOrderChangeCommandHandler(dbContext, new CapturingPurchaseOrderApprovalClient());
        var command = new RequestPurchaseOrderChangeCommand(
            "org-001",
            "env-dev",
            "PO-CHANGE-001",
            [new PurchaseOrderLineChangeDraft("LINE-001", 4m, 12m, new DateOnly(2026, 6, 7))]);

        var firstChainId = await handler.Handle(command, CancellationToken.None);
        var secondChainId = await handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(firstChainId, secondChainId);
    }

    [Fact]
    public async Task Rejected_purchase_order_change_endpoint_revises_and_resubmits_the_source_order()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = PurchaseOrder.Create("org-001", "env-dev", "PO-REVISE-001", "SUP-001", "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]);
        order.MarkApprovalRequested("approval-rejected-001");
        order.ReturnToEditableAfterApprovalRejected("approval-rejected-001");
        dbContext.PurchaseOrders.Add(order);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var approvalClient = new CapturingPurchaseOrderApprovalClient();

        var chainId = await new RequestPurchaseOrderChangeCommandHandler(dbContext, approvalClient).Handle(
            new RequestPurchaseOrderChangeCommand("org-001", "env-dev", "PO-REVISE-001",
                [new PurchaseOrderLineChangeDraft("LINE-001", 5m, 14m, new DateOnly(2026, 7, 20))], "revise after rejection", "user:buyer-001"),
            CancellationToken.None);

        Assert.Equal(chainId, order.ApprovalChainId);
        Assert.Equal(70m, order.TotalAmount);
        Assert.Equal(70m, approvalClient.LastRequest!.Amount);
    }

    [Fact]
    public async Task Cancelling_a_purchase_order_closes_its_open_wms_inbound_expectations_first()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-CANCEL-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]);
        order.MarkApprovalRequested("approval-create-001");
        order.ReleaseAfterApproval("approval-create-001");
        dbContext.PurchaseOrders.Add(order);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var wms = new CapturingWmsInboundCancellationClient();

        await new CancelPurchaseOrderCommandHandler(dbContext, wms).Handle(
            new CancelPurchaseOrderCommand("org-001", "env-dev", "PO-CANCEL-001", "supplier withdrew order"),
            CancellationToken.None);

        Assert.Equal("PO-CANCEL-001", wms.PurchaseOrderNo);
        Assert.Equal("supplier withdrew order", wms.Reason);
        Assert.Equal(PurchaseOrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task Purchase_order_cancellation_does_not_close_wms_expectations_when_receipts_already_exist()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = PurchaseOrder.Create(
            "org-001", "env-dev", "PO-RECEIVED-001", "SUP-001", "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]);
        order.MarkApprovalRequested("approval-create-001");
        order.ReleaseAfterApproval("approval-create-001");
        order.RegisterReceipt("LINE-001", 1m);
        dbContext.PurchaseOrders.Add(order);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var wms = new CapturingWmsInboundCancellationClient();

        await Assert.ThrowsAsync<KnownException>(() => new CancelPurchaseOrderCommandHandler(dbContext, wms).Handle(
            new CancelPurchaseOrderCommand("org-001", "env-dev", "PO-RECEIVED-001", "supplier withdrew order"),
            CancellationToken.None));

        Assert.Null(wms.PurchaseOrderNo);
        Assert.Equal(PurchaseOrderStatus.Released, order.Status);
    }

    [Fact]
    public async Task Convert_purchase_requisitions_without_price_source_creates_rfq_and_keeps_requisitions_open()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedPurchaseRequisitionAsync(dbContext, "PR-001", "suggestion-001", "SKU-RM-1000", 3m);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler(dbContext).Handle(
            new ConvertPurchaseRequisitionsToPurchaseOrderCommand(
                "org-001",
                "env-dev",
                ["PR-001"],
                RfqNo: "RFQ-FOLLOW-001",
                RfqSupplierCodes: ["SUP-001", "SUP-002"]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(PurchaseRequisitionConversionStatus.RfqCreated, result.Status);
        Assert.Equal("RFQ-FOLLOW-001", result.RfqNo);
        Assert.Empty(dbContext.PurchaseOrders);
        Assert.Equal(PurchaseRequisitionStatus.Open, Assert.Single(dbContext.PurchaseRequisitions).Status);
        var rfq = Assert.Single(dbContext.RequestForQuotations.Include(x => x.Lines).Include(x => x.Suppliers));
        Assert.Equal("RFQ-FOLLOW-001", rfq.RfqNo);
        Assert.Contains(rfq.Suppliers, supplier => supplier.SupplierCode == "SUP-002");
        Assert.Equal("SKU-RM-1000", Assert.Single(rfq.Lines).SkuCode);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"erp-procurement-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static async Task SeedPurchaseRequisitionAsync(
        ApplicationDbContext dbContext,
        string requisitionNo,
        string suggestionId,
        string skuCode,
        decimal quantity)
    {
        await new CreatePurchaseRequisitionFromSuggestionCommandHandler(dbContext).Handle(
            new CreatePurchaseRequisitionFromSuggestionCommand(
                "org-001",
                "env-dev",
                requisitionNo,
                suggestionId,
                skuCode,
                "kg",
                "SITE-01",
                quantity,
                new DateOnly(2026, 6, 5)),
            CancellationToken.None);
    }

    private sealed class CapturingPurchaseOrderApprovalClient : IPurchaseOrderApprovalClient
    {
        public PurchaseOrderApprovalRequest? LastRequest { get; private set; }

        public Task<PurchaseOrderApprovalResult> StartApprovalAsync(PurchaseOrderApprovalRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new PurchaseOrderApprovalResult(request.ChainId));
        }
    }

    private sealed class CapturingWmsInboundCancellationClient : IWmsInboundCancellationClient
    {
        public string? PurchaseOrderNo { get; private set; }
        public string? Reason { get; private set; }

        public Task CancelOpenInboundOrdersForPurchaseOrderAsync(
            string organizationId,
            string environmentId,
            string purchaseOrderNo,
            string reason,
            CancellationToken cancellationToken)
        {
            PurchaseOrderNo = purchaseOrderNo;
            Reason = reason;
            return Task.CompletedTask;
        }
    }

    private static void SetRfqCreatedAt(ApplicationDbContext dbContext, string rfqNo, DateTime createdAtUtc)
    {
        var rfq = dbContext.RequestForQuotations.Single(x => x.RfqNo == rfqNo);
        dbContext.Entry(rfq).Property(x => x.CreatedAtUtc).CurrentValue = createdAtUtc;
    }
}
