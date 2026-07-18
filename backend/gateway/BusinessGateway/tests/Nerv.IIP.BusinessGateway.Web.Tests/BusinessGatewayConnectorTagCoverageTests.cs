using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Telemetry;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayConnectorTagCoverageTests
{
    [Fact]
    public async Task Coverage_facade_authorizes_connector_scope_and_forwards_canonical_context_with_internal_token()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var telemetry = new RecordingIndustrialTelemetryClient
        {
            ConnectorTagCoverageResponse = new BusinessConsoleConnectorTagCoverageResponse(
                "opc-main",
                "current",
                "manifest-revision-001",
                DateTimeOffset.Parse("2026-07-17T08:00:00Z"),
                1,
                1,
                1,
                0,
                0,
                [
                    new BusinessConsoleConnectorTagCoverageItem(
                        "device-001",
                        "temperature",
                        true,
                        "active",
                        DateTimeOffset.Parse("2026-07-17T08:01:00Z"),
                        null,
                        null,
                        null,
                        null),
                ]),
        };
        await using var factory = CreateFactory(auth, telemetry);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            "/api/business-console/v1/telemetry/connectors/opc-main/tag-coverage?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.IiotTelemetryRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("connector", auth.LastRequirement.ResourceType);
        Assert.Equal("opc-main", auth.LastRequirement.ResourceId);
        Assert.Equal("internal-telemetry-token", telemetry.LastInternalToken);
        Assert.Equal(
            new BusinessConsoleConnectorTagCoverageRequest("opc-main", "org-001", "env-dev"),
            telemetry.LastConnectorTagCoverageRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("current", data.GetProperty("manifestStatus").GetString());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("firstSampleAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("lastSampleAtUtc").ValueKind);
    }

    [Theory]
    [InlineData("unavailable", null, null)]
    [InlineData("current", "manifest-revision-empty", "2026-07-17T09:00:00Z")]
    public async Task Coverage_facade_preserves_unavailable_and_current_empty_states(
        string manifestStatus,
        string? manifestRevision,
        string? manifestObservedAtUtc)
    {
        var telemetry = new RecordingIndustrialTelemetryClient
        {
            ConnectorTagCoverageResponse = new BusinessConsoleConnectorTagCoverageResponse(
                "connector-empty",
                manifestStatus,
                manifestRevision,
                manifestObservedAtUtc is null ? null : DateTimeOffset.Parse(manifestObservedAtUtc),
                0,
                0,
                0,
                0,
                0,
                []),
        };
        await using var factory = CreateFactory(FakeBusinessGatewayAuthorizationClient.Allowed(), telemetry);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync(
            "/api/business-console/v1/telemetry/connectors/connector-empty/tag-coverage?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(manifestStatus, data.GetProperty("manifestStatus").GetString());
        Assert.Equal(manifestRevision, data.GetProperty("manifestRevision").GetString());
        Assert.Equal(
            manifestObservedAtUtc is null ? JsonValueKind.Null : JsonValueKind.String,
            data.GetProperty("manifestObservedAtUtc").ValueKind);
        Assert.Empty(data.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void Coverage_request_validator_requires_route_and_query_scope()
    {
        var result = new BusinessConsoleConnectorTagCoverageRequestValidator().Validate(
            new BusinessConsoleConnectorTagCoverageRequest(" ", "", ""));

        Assert.Contains(result.Errors, error => PropertyMatches(error.PropertyName, nameof(BusinessConsoleConnectorTagCoverageRequest.ConnectorId)));
        Assert.Contains(result.Errors, error => PropertyMatches(error.PropertyName, nameof(BusinessConsoleConnectorTagCoverageRequest.OrganizationId)));
        Assert.Contains(result.Errors, error => PropertyMatches(error.PropertyName, nameof(BusinessConsoleConnectorTagCoverageRequest.EnvironmentId)));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeBusinessGatewayAuthorizationClient auth,
        RecordingIndustrialTelemetryClient telemetry) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
                services.RemoveAll<IBusinessIndustrialTelemetryClient>();
                services.AddSingleton<IBusinessIndustrialTelemetryClient>(telemetry);
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(
                    new TestInternalServiceTokenProvider("internal-telemetry-token"));
            });
        });

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private static bool PropertyMatches(string actual, string expected) =>
        string.Equals(
            actual.Replace(" ", string.Empty, StringComparison.Ordinal),
            expected,
            StringComparison.OrdinalIgnoreCase);
}
