using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Auth;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.Scans;
using Nerv.IIP.Business.BarcodeLabel.Web.Endpoints.BarcodeLabel;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelEndpointContractTests
{
    [Fact]
    public void BarcodeLabel_endpoints_expose_issue_133_routes_permissions_policies_and_operation_ids()
    {
        var contracts = BarcodeLabelEndpointContracts.All.ToArray();

        Assert.Equal(12, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/barcodes/rules"
            && x.PermissionCode == BarcodeLabelPermissionCodes.TemplatesManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listBusinessBarcodeRules");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/barcodes/rules"
            && x.PermissionCode == BarcodeLabelPermissionCodes.TemplatesManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createOrUpdateBusinessBarcodeRule");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/barcodes/templates"
            && x.PermissionCode == BarcodeLabelPermissionCodes.TemplatesManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createOrUpdateBusinessBarcodeTemplate");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/barcodes/templates"
            && x.PermissionCode == BarcodeLabelPermissionCodes.TemplatesManage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listBusinessBarcodeTemplates");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/barcodes/print-batches"
            && x.PermissionCode == BarcodeLabelPermissionCodes.Print
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createBusinessBarcodePrintBatch");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/barcodes/print-batches/{printBatchId}/dispatch"
            && x.PermissionCode == BarcodeLabelPermissionCodes.Print
            && x.OperationId == "dispatchBusinessBarcodePrintBatch");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/barcodes/print-batches/{printBatchId}/items/{sequenceNo}/reprint"
            && x.PermissionCode == BarcodeLabelPermissionCodes.Print
            && x.OperationId == "reprintBusinessBarcodeLabel");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/barcodes/print-batches/{printBatchId}/items/{sequenceNo}/void"
            && x.PermissionCode == BarcodeLabelPermissionCodes.Print
            && x.OperationId == "voidBusinessBarcodeLabel");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/barcodes/print-batches"
            && x.PermissionCode == BarcodeLabelPermissionCodes.Print
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listBusinessBarcodePrintBatches");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/barcodes/print-batches/{printBatchId}"
            && x.PermissionCode == BarcodeLabelPermissionCodes.Print
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "getBusinessBarcodePrintBatch");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/barcodes/scans"
            && x.PermissionCode == BarcodeLabelPermissionCodes.ScansWrite
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "recordBusinessBarcodeScan");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/barcodes/scans"
            && x.PermissionCode == BarcodeLabelPermissionCodes.ScansWrite
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listBusinessBarcodeScans");
    }

    [Theory]
    [InlineData(typeof(ListBarcodeRulesEndpoint))]
    [InlineData(typeof(CreateOrUpdateBarcodeRuleEndpoint))]
    [InlineData(typeof(CreateOrUpdateLabelTemplateEndpoint))]
    [InlineData(typeof(ListLabelTemplatesEndpoint))]
    [InlineData(typeof(CreateLabelPrintBatchEndpoint))]
    [InlineData(typeof(DispatchLabelPrintBatchEndpoint))]
    [InlineData(typeof(ReprintLabelEndpoint))]
    [InlineData(typeof(VoidLabelEndpoint))]
    [InlineData(typeof(ListLabelPrintBatchesEndpoint))]
    [InlineData(typeof(GetLabelPrintBatchEndpoint))]
    [InlineData(typeof(RecordScanEndpoint))]
    [InlineData(typeof(ListScansEndpoint))]
    public void BarcodeLabel_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
    }

    [Fact]
    public void Validators_reject_missing_print_idempotency_key()
    {
        var result = new CreateLabelPrintBatchCommandValidator().Validate(new CreateLabelPrintBatchCommand(
            "org-001",
            "env-dev",
            new(Guid.CreateVersion7()),
            new(Guid.CreateVersion7()),
            "wms.inbound",
            "ASN-001",
            "",
            """{"sku":"SKU-FG-1000"}""",
            1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => SameProperty(x.PropertyName, nameof(CreateLabelPrintBatchCommand.IdempotencyKey)));
    }

    [Fact]
    public void Validators_reject_missing_scan_device_and_scanned_value()
    {
        var result = new RecordScanCommandValidator().Validate(new RecordScanCommand(
            "org-001",
            "env-dev",
            "",
            "",
            "wms.receiving",
            "ASN-001",
            "idem-scan-001",
            "accepted",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => SameProperty(x.PropertyName, nameof(RecordScanCommand.DeviceCode)));
        Assert.Contains(result.Errors, x => SameProperty(x.PropertyName, nameof(RecordScanCommand.ScannedValue)));
    }

    [Fact]
    public void Validators_require_inventory_context_for_accepted_inventory_scan()
    {
        var result = new RecordScanCommandValidator().Validate(new RecordScanCommand(
            "org-001",
            "env-dev",
            "PDA-01",
            "(01)09506000134352(10)LOT-A(21)SN-0001",
            "inventory.receipt",
            "ASN-001",
            "idem-scan-gs1-001",
            "accepted",
            null,
            null,
            "EA",
            "SITE-01",
            "STAGE-01",
            "qualified",
            "owned",
            null,
            1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => SameProperty(x.PropertyName, nameof(RecordScanCommand.SkuCode)));
    }

    [Fact]
    public void Validators_reject_unsupported_accepted_scan_workflow()
    {
        var result = new RecordScanCommandValidator().Validate(new RecordScanCommand(
            "org-001",
            "env-dev",
            "PDA-01",
            "BC001",
            "mes.report",
            "WO-001",
            "idem-scan-unsupported-001",
            "accepted",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => SameProperty(x.PropertyName, nameof(RecordScanCommand.SourceWorkflow)));
    }

    [Fact]
    public async Task BarcodeLabel_http_endpoints_reject_anonymous_callers_before_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/business/v1/barcodes/scans", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceCode = "PDA-01",
            scannedValue = "BC001",
            sourceWorkflow = "wms.receiving",
            sourceDocumentId = "ASN-001",
            idempotencyKey = "idem-scan-001",
            result = "accepted",
            rejectionReason = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Public_responses_do_not_expose_filestorage_object_keys()
    {
        var responseTypes = typeof(BarcodeLabelEndpointContracts).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(BarcodeLabelEndpointContracts).Namespace)
            .Where(type => type.Name.EndsWith("Response", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(responseTypes);
        var publicNames = responseTypes
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(publicNames, name => string.Equals(name, "ObjectKey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicNames, name => string.Equals(name, "Object_Key", StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameProperty(string actual, string expected)
    {
        return string.Equals(actual.Replace(" ", string.Empty, StringComparison.Ordinal), expected, StringComparison.OrdinalIgnoreCase);
    }
}
