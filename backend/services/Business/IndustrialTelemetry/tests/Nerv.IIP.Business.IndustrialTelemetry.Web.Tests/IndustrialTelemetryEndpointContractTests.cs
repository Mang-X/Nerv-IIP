using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Auth;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Endpoints.Iiot;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryEndpointContractTests
{
    [Fact]
    public void IndustrialTelemetry_endpoints_expose_issue_129_routes_permissions_policies_and_operation_ids()
    {
        var contracts = IndustrialTelemetryEndpointContracts.All.ToArray();

        Assert.Equal(6, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/iiot/tags" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TagsManage && x.OperationId == "createBusinessIiotTelemetryTag");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/tags" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead && x.OperationId == "listBusinessIiotTelemetryTags");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/iiot/samples" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryWrite && x.OperationId == "recordBusinessIiotTelemetrySample");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/iiot/alarms" && x.PermissionCode == IndustrialTelemetryPermissionCodes.AlarmsWrite && x.OperationId == "raiseBusinessIiotAlarm");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/alarms" && x.PermissionCode == IndustrialTelemetryPermissionCodes.AlarmsRead && x.OperationId == "listBusinessIiotAlarms");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/devices/{deviceAssetId}/timeline" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead && x.OperationId == "queryBusinessIiotDeviceTimeline");
        Assert.All(contracts, x => Assert.Equal(InternalServiceAuthorizationPolicy.Name, x.AuthorizationPolicy));
    }

    [Theory]
    [MemberData(nameof(EndpointTypes))]
    public void IndustrialTelemetry_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType.GetConstructors().Single().GetParameters().Select(x => x.ParameterType).ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public void Validators_reject_missing_tag_and_sequence_facts()
    {
        var tagResult = new CreateTelemetryTagCommandValidator().Validate(new CreateTelemetryTagCommand("org-001", "env-dev", "", "", "number", "rpm", "sample-10s"));
        var sampleResult = new RecordTelemetrySampleCommandValidator().Validate(new RecordTelemetrySampleCommand("org-001", "env-dev", "DEV-CNC-01", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), 1, 1, 2, 1.5m, ""));

        Assert.False(tagResult.IsValid);
        Assert.False(sampleResult.IsValid);
        Assert.Contains(tagResult.Errors, x => SameProperty(x.PropertyName, nameof(CreateTelemetryTagCommand.DeviceAssetId)));
        Assert.Contains(tagResult.Errors, x => SameProperty(x.PropertyName, nameof(CreateTelemetryTagCommand.TagKey)));
        Assert.Contains(sampleResult.Errors, x => SameProperty(x.PropertyName, nameof(RecordTelemetrySampleCommand.TagKey)));
        Assert.Contains(sampleResult.Errors, x => SameProperty(x.PropertyName, nameof(RecordTelemetrySampleCommand.SourceSequence)));
    }

    [Fact]
    public async Task IndustrialTelemetry_http_endpoints_reject_anonymous_callers_before_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/business/v1/iiot/alarms", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId = "DEV-CNC-01",
            alarmCode = "OVER_TEMP",
            severity = "critical",
            raisedAtUtc = DateTimeOffset.UtcNow,
            externalAlarmId = "alarm-ext-001",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Public_contracts_do_not_expose_control_payload_credential_or_scada_concepts()
    {
        var publicNames = typeof(IndustrialTelemetryEndpointContracts).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(IndustrialTelemetryEndpointContracts).Namespace)
            .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        var forbidden = new[] { "Control", "CommandPayload", "Credential", "Secret", "Password", "Scada" };
        Assert.NotEmpty(publicNames);
        Assert.DoesNotContain(publicNames, name => forbidden.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    public static IEnumerable<object[]> EndpointTypes()
    {
        return IndustrialTelemetryEndpointContracts.All.Select(x => new object[] { x.EndpointType });
    }

    private static bool SameProperty(string actual, string expected)
    {
        return string.Equals(actual.Replace(" ", string.Empty, StringComparison.Ordinal), expected, StringComparison.OrdinalIgnoreCase);
    }
}
