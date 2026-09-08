using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayBarcodeLifecycleEndpointTests
{
    [Fact]
    public async Task Dispatch_authorizes_and_forwards_the_route_batch_with_internal_token()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = Lease(auth, barcode);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/barcode/print-batches/batch-route/dispatch?organizationId=org-001&environmentId=env-dev",
            new { printBatchId = "batch-body", printerId = "printer-01" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.BarcodePrint, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Equal("barcode-print-batch", auth.LastRequirement.ResourceType);
        Assert.Equal("batch-route", auth.LastRequirement.ResourceId);
        Assert.Equal("internal-test-token", barcode.LastInternalToken);
        Assert.Equal("batch-route", barcode.LastDispatchRequest!.Body.PrintBatchId);
        Assert.Equal("printer-01", barcode.LastDispatchRequest.Body.PrinterId);
    }

    [Fact]
    public async Task Reprint_authorizes_and_forwards_route_batch_and_sequence()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = Lease(auth, barcode);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/barcode/print-batches/batch-route/items/7/reprint?organizationId=org-001&environmentId=env-dev",
            new { printBatchId = "batch-body", sequenceNo = 99, printerId = "printer-01" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("batch-route", auth.LastRequirement!.ResourceId);
        Assert.Equal("batch-route", barcode.LastReprintRequest!.Body.PrintBatchId);
        Assert.Equal(7, barcode.LastReprintRequest.Body.SequenceNo);
        Assert.Equal("printer-01", barcode.LastReprintRequest.Body.PrinterId);
    }

    [Fact]
    public async Task Void_authorizes_and_forwards_route_batch_and_sequence()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = Lease(auth, barcode);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/barcode/print-batches/batch-route/items/7/void?organizationId=org-001&environmentId=env-dev",
            new { printBatchId = "batch-body", sequenceNo = 99, reason = "damaged" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("batch-route", auth.LastRequirement!.ResourceId);
        Assert.Equal("batch-route", barcode.LastVoidRequest!.Body.PrintBatchId);
        Assert.Equal(7, barcode.LastVoidRequest.Body.SequenceNo);
        Assert.Equal("damaged", barcode.LastVoidRequest.Body.Reason);
    }

    [Theory]
    [InlineData("dispatch")]
    [InlineData("reprint")]
    [InlineData("void")]
    public async Task Lifecycle_route_requires_authentication_before_authorization_or_downstream(string operation)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = Lease(auth, barcode);

        var response = await SendLifecycleAsync(
            lease.CreateClient(),
            operation,
            "organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        Assert.Equal(0, barcode.LifecycleCallCount);
    }

    [Theory]
    [InlineData("dispatch")]
    [InlineData("reprint")]
    [InlineData("void")]
    public async Task Lifecycle_route_fails_closed_when_resource_authorization_is_denied(string operation)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = Lease(auth, barcode);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await SendLifecycleAsync(
            client,
            operation,
            "organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("barcode-print-batch", auth.LastRequirement!.ResourceType);
        Assert.Equal("batch-001", auth.LastRequirement.ResourceId);
        Assert.Equal(0, barcode.LifecycleCallCount);
    }

    [Theory]
    [InlineData("dispatch", "organizationId=org-001")]
    [InlineData("dispatch", "environmentId=env-dev")]
    [InlineData("reprint", "organizationId=org-001")]
    [InlineData("reprint", "environmentId=env-dev")]
    [InlineData("void", "organizationId=org-001")]
    [InlineData("void", "environmentId=env-dev")]
    public async Task Lifecycle_route_requires_both_scope_query_values_before_authorization_or_downstream(
        string operation,
        string query)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = Lease(auth, barcode);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await SendLifecycleAsync(client, operation, query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        Assert.Equal(0, barcode.LifecycleCallCount);
    }

    [Theory]
    [InlineData("dispatch")]
    [InlineData("reprint")]
    [InlineData("void")]
    public async Task Lifecycle_route_rejects_principal_scope_mismatch_without_downstream_call(string operation)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = Lease(auth, barcode);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await SendLifecycleAsync(
            client,
            operation,
            "organizationId=org-other&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        Assert.Equal(0, barcode.LifecycleCallCount);
    }

    [Theory]
    [InlineData("reprint")]
    [InlineData("void")]
    public async Task Label_item_route_rejects_non_integer_sequence_before_authorization_or_downstream(string operation)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = Lease(auth, barcode);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        object requestBody = operation == "reprint"
            ? new { printBatchId = "batch-001", sequenceNo = 7, printerId = "printer-01" }
            : new { printBatchId = "batch-001", sequenceNo = 7, reason = "damaged" };
        var response = await client.PostAsJsonAsync(
            $"/api/business-console/v1/barcode/print-batches/batch-001/items/not-an-integer/{operation}?organizationId=org-001&environmentId=env-dev",
            requestBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
        Assert.Equal(0, barcode.LifecycleCallCount);
    }

    [Theory]
    [InlineData("dispatch")]
    [InlineData("reprint")]
    [InlineData("void")]
    public async Task Lifecycle_route_does_not_expose_sensitive_downstream_exception_text(string operation)
    {
        const string sensitive = "tcp://printer.internal:9100?token=secret-value";
        var barcode = new RecordingBarcodeLabelClient
        {
            LifecycleFailure = new BusinessServiceProxyException(
                HttpStatusCode.BadGateway,
                sensitive,
                new InvalidOperationException(sensitive)),
        };
        await using var lease = Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), barcode);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await SendLifecycleAsync(
            client,
            operation,
            "organizationId=org-001&environmentId=env-dev");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain(sensitive, body, StringComparison.Ordinal);
        Assert.Contains(BusinessServiceProxyException.DownstreamRequestFailedMessage, body, StringComparison.Ordinal);
    }

    private static BusinessGatewayTestHostLease Lease(
        FakeBusinessGatewayAuthorizationClient auth,
        RecordingBarcodeLabelClient barcode) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessBarcodeLabelClient>();
            services.AddSingleton<IBusinessBarcodeLabelClient>(barcode);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(
                new TestInternalServiceTokenProvider("internal-test-token"));
        });

    private static Task<HttpResponseMessage> SendLifecycleAsync(
        HttpClient client,
        string operation,
        string query) => operation switch
        {
            "dispatch" => client.PostAsJsonAsync(
                $"/api/business-console/v1/barcode/print-batches/batch-001/dispatch?{query}",
                new { printBatchId = "batch-001", printerId = "printer-01" }),
            "reprint" => client.PostAsJsonAsync(
                $"/api/business-console/v1/barcode/print-batches/batch-001/items/7/reprint?{query}",
                new { printBatchId = "batch-001", sequenceNo = 7, printerId = "printer-01" }),
            "void" => client.PostAsJsonAsync(
                $"/api/business-console/v1/barcode/print-batches/batch-001/items/7/void?{query}",
                new { printBatchId = "batch-001", sequenceNo = 7, reason = "damaged" }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
}
