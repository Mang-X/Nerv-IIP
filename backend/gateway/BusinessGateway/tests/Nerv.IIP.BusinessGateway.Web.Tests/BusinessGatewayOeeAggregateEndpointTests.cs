using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Contracts.IndustrialTelemetry;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayOeeAggregateEndpointTests
{
    private static readonly DateTimeOffset WindowStartUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset WindowEndUtc = DateTimeOffset.Parse("2026-06-01T16:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Oee_aggregate_facade_requires_realtime_principal_scope_and_preserves_the_public_contract()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(
            scopeGrants:
            [
                new AuthorizationScopeGrant(
                    "role",
                    "role-operations-reader",
                    "organization",
                    "org-001",
                    [BusinessGatewayPermissions.IiotTelemetryRead],
                    OrganizationWide: true),
            ]);
        var capability = new RecordingOeeAggregateCapability();
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessOeeAggregateCapability>();
            services.AddSingleton<IBusinessOeeAggregateCapability>(capability);
        });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var response = await client.GetAsync(
            "/api/business-console/v1/telemetry/oee/aggregates"
            + "?organizationId=org-001&environmentId=env-dev&dimension=workCenter"
            + "&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z"
            + "&deviceAssetId=DEV-001&workCenterId=WC-001&shiftCode=SHIFT-A"
            + "&lineCode=LINE-001&workshopCode=SHOP-001&businessDate=2026-06-01&skip=5&take=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.IiotTelemetryRead, auth.LastRequirement!.PermissionCode);
        Assert.True(auth.LastRequirement.IncludePrincipalContext);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
        Assert.Equal(
            new QueryOeeAggregateBucketsRequest(
                "org-001",
                "env-dev",
                OeeAggregateDimension.WorkCenter,
                WindowStartUtc,
                WindowEndUtc,
                "DEV-001",
                "WC-001",
                "SHIFT-A",
                "LINE-001",
                "SHOP-001",
                new DateOnly(2026, 6, 1),
                5,
                25),
            capability.LastRequest);
        Assert.Equal("user-admin", capability.LastAuthorization!.PrincipalId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("workCenter", data.GetProperty("dimension").GetString());
        var bucket = data.GetProperty("buckets")[0];
        Assert.Equal(0.684m, bucket.GetProperty("oeeRate").GetDecimal());
        Assert.True(bucket.GetProperty("isDegraded").GetBoolean());
        Assert.Equal(
            "runtimeStateCoverageIncomplete",
            bucket.GetProperty("degradedReasons")[0].GetString());
    }

    [Fact]
    public async Task Oee_aggregate_facade_does_not_reach_the_capability_when_permission_is_denied()
    {
        var capability = new RecordingOeeAggregateCapability();
        await using var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Forbidden(),
            services =>
            {
                services.RemoveAll<IBusinessOeeAggregateCapability>();
                services.AddSingleton<IBusinessOeeAggregateCapability>(capability);
            });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var response = await client.GetAsync(
            "/api/business-console/v1/telemetry/oee/aggregates"
            + "?organizationId=org-001&environmentId=env-dev&dimension=day"
            + "&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, capability.CallCount);
    }

    [Fact]
    public async Task Oee_aggregate_facade_maps_invalid_downstream_facts_to_bad_gateway()
    {
        var capability = new RecordingOeeAggregateCapability
        {
            Failure = BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response"),
        };
        await using var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services =>
            {
                services.RemoveAll<IBusinessOeeAggregateCapability>();
                services.AddSingleton<IBusinessOeeAggregateCapability>(capability);
            });
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        using var response = await client.GetAsync(
            "/api/business-console/v1/telemetry/oee/aggregates"
            + "?organizationId=org-001&environmentId=env-dev&dimension=day"
            + "&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("downstream-invalid-response", document.RootElement.GetProperty("message").GetString());
    }

    private sealed class RecordingOeeAggregateCapability : IBusinessOeeAggregateCapability
    {
        public int CallCount { get; private set; }

        public BusinessGatewayAuthorizationResult? LastAuthorization { get; private set; }

        public QueryOeeAggregateBucketsRequest? LastRequest { get; private set; }

        public BusinessServiceProxyException? Failure { get; init; }

        public Task<OeeAggregateBucketsResponse> QueryAsync(
            BusinessGatewayAuthorizationResult authorization,
            QueryOeeAggregateBucketsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastAuthorization = authorization;
            LastRequest = request;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new OeeAggregateBucketsResponse(
                request.OrganizationId,
                request.EnvironmentId,
                request.Dimension,
                request.WindowStartUtc,
                request.WindowEndUtc,
                [
                    new OeeAggregateBucket(
                        request.Dimension,
                        "WC-001",
                        "SITE-001",
                        request.WorkshopCode,
                        request.LineCode,
                        request.WorkCenterId,
                        request.DeviceAssetId,
                        request.ShiftCode,
                        request.BusinessDate,
                        request.WindowStartUtc,
                        request.WindowEndUtc,
                        1,
                        2,
                        3,
                        0.9m,
                        0.8m,
                        0.95m,
                        0.684m,
                        10m,
                        1m,
                        0m,
                        "PCS",
                        12m,
                        true,
                        [OeeAggregateDegradedReason.RuntimeStateCoverageIncomplete]),
                ],
                1,
                request.Skip,
                request.Take));
        }
    }
}
