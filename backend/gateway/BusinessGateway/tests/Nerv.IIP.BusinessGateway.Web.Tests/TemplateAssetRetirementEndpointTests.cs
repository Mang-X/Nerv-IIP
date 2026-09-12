using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Barcode;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// PublicContract: #3043 requires realtime IAM, no client-controlled proof identity, and no forwarding on denial.
public sealed class TemplateAssetRetirementEndpointTests
{
    // PublicContract #3049 A: the read uses the retirement permission and never needs a signing secret.
    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    public async Task Retirement_read_requires_its_own_permission_without_signing(bool allowed, HttpStatusCode expected)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(allowed
            ? BusinessGatewayPermissions.BarcodeTemplateAssetsRetire
            : BusinessGatewayPermissions.BarcodeTemplatesManage);
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessBarcodeLabelClient>();
            services.AddSingleton<IBusinessBarcodeLabelClient>(barcode);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-read-token"));
            services.Configure<BusinessGatewayTemplateAssetRetirementProofOptions>(options => options.SecretBase64 = "");
        });
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var templateId = Guid.NewGuid();
        using var response = await client.GetAsync(
            $"/api/business-console/v1/barcode/template-assets/retirement?organizationId=org-001&environmentId=env-dev&templateId={templateId:D}&fileId=file-old");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.BarcodeTemplateAssetsRetire, auth.LastRequirement!.PermissionCode);
        Assert.Equal(templateId.ToString("D"), auth.LastRequirement.ResourceId);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        Assert.Null(barcode.LastRetirementRequest);
        if (allowed)
        {
            Assert.Equal("internal-read-token", barcode.LastInternalToken);
            Assert.Equal(templateId, barcode.LastRetirementReadRequest!.TemplateId);
            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains("quota-released", json, StringComparison.Ordinal);
            Assert.DoesNotContain("proof", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reason", json, StringComparison.OrdinalIgnoreCase);
        }
        else Assert.Null(barcode.LastRetirementReadRequest);
    }

    [Theory]
    [InlineData("allowed", HttpStatusCode.OK)]
    [InlineData("forbidden", HttpStatusCode.Forbidden)]
    [InlineData("missing-subject", HttpStatusCode.Forbidden)]
    [InlineData("other-permission", HttpStatusCode.Forbidden)]
    public async Task Retirement_uses_its_own_realtime_permission_and_only_signs_the_authorized_subject(
        string mode, HttpStatusCode expected)
    {
        var auth = mode switch
        {
            "allowed" => new FakeBusinessGatewayAuthorizationClient(_ => true,
                allowedResult: BusinessGatewayAuthorizationResult.Allowed(
                    "user-other", "user", "other", "org-001", "env-dev")),
            "missing-subject" => FakeBusinessGatewayAuthorizationClient.AllowedWithoutPrincipal(),
            "other-permission" => FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.BarcodeTemplatesManage),
            _ => FakeBusinessGatewayAuthorizationClient.Forbidden(),
        };
        var barcode = new RecordingBarcodeLabelClient();
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessBarcodeLabelClient>();
            services.AddSingleton<IBusinessBarcodeLabelClient>(barcode);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
            services.Configure<BusinessGatewayTemplateAssetRetirementProofOptions>(options =>
            {
                options.Issuer = "gateway-test";
                options.Audience = "barcode-test";
                options.SecretBase64 = Convert.ToBase64String(new byte[32]);
            });
        });
        using var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var request = new RetireBusinessConsoleBarcodeTemplateAssetRequest("org-001", "env-dev",
            Guid.NewGuid(), "file-old", "sha256:" + new string('a', 64), "旧资产\n退役", "retire-3043");
        using var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/barcode/template-assets/retire", request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        Assert.Equal("business.barcodes.template-assets.retire", auth.LastRequirement!.PermissionCode);
        Assert.Equal(request.TemplateId.ToString("D"), auth.LastRequirement.ResourceId);
        if (expected != HttpStatusCode.OK)
        {
            Assert.Null(barcode.LastRetirementRequest);
            return;
        }
        Assert.Equal("internal-test-token", barcode.LastInternalToken);
        var signed = Assert.IsType<Nerv.IIP.Contracts.BarcodeLabel.RetireTemplateAssetRequest>(barcode.LastRetirementRequest);
        Assert.Equal(request.Reason, signed.Reason);
        var encoded = signed.Proof.Split('.')[0];
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(
            encoded.Replace('-', '+').Replace('_', '/') + new string('=', (4 - encoded.Length % 4) % 4)));
        Assert.Equal("10:user-other", payload.Split('\n')[6]);
        Assert.Contains("business.barcodes.template-assets.retire", payload, StringComparison.Ordinal);
    }
}
