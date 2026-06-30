using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Auth;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Endpoints.Iiot;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryEndpointContractTests
{
    [Fact]
    public void IndustrialTelemetry_endpoints_expose_issue_129_routes_permissions_policies_and_operation_ids()
    {
        var contracts = IndustrialTelemetryEndpointContracts.All.ToArray();

        Assert.Equal(12, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/iiot/tags" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TagsManage && x.OperationId == "createBusinessIiotTelemetryTag");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/tags" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead && x.OperationId == "listBusinessIiotTelemetryTags");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/iiot/alarm-rules" && x.PermissionCode == "business.iiot.alarm-rules.manage" && x.OperationId == "createOrUpdateBusinessIiotAlarmRule");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/alarm-rules" && x.PermissionCode == "business.iiot.alarms.read" && x.OperationId == "listBusinessIiotAlarmRules");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/iiot/samples" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryWrite && x.OperationId == "recordBusinessIiotTelemetrySample");
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/iiot/alarms" && x.PermissionCode == IndustrialTelemetryPermissionCodes.AlarmsWrite && x.OperationId == "raiseBusinessIiotAlarm");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/alarms" && x.PermissionCode == IndustrialTelemetryPermissionCodes.AlarmsRead && x.OperationId == "listBusinessIiotAlarms");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/devices/{deviceAssetId}/timeline" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead && x.OperationId == "queryBusinessIiotDeviceTimeline");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/oee" && x.PermissionCode == "business.iiot.telemetry.read" && x.OperationId == "queryBusinessIiotOee");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/devices/{deviceAssetId}/runtime-availability" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead && x.OperationId == "getBusinessIiotDeviceRuntimeAvailability");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/runtime-availability" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead && x.OperationId == "queryBusinessIiotRuntimeAvailability");
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/iiot/devices/{deviceAssetId}/current-state" && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead && x.OperationId == "getBusinessIiotDeviceCurrentState");
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
        var alarmRuleResult = new CreateOrUpdateAlarmRuleCommandValidator().Validate(new CreateOrUpdateAlarmRuleCommand("org-001", "env-dev", "DEV-CNC-01", "TEMP_RULE", "TEMP_HIGH", "warning", "temperature", "contains", 95m, "celsius", true));

        Assert.False(tagResult.IsValid);
        Assert.False(sampleResult.IsValid);
        Assert.False(alarmRuleResult.IsValid);
        Assert.Contains(tagResult.Errors, x => SameProperty(x.PropertyName, nameof(CreateTelemetryTagCommand.DeviceAssetId)));
        Assert.Contains(tagResult.Errors, x => SameProperty(x.PropertyName, nameof(CreateTelemetryTagCommand.TagKey)));
        Assert.Contains(sampleResult.Errors, x => SameProperty(x.PropertyName, nameof(RecordTelemetrySampleCommand.TagKey)));
        Assert.Contains(sampleResult.Errors, x => SameProperty(x.PropertyName, nameof(RecordTelemetrySampleCommand.SourceSequence)));
        Assert.Contains(alarmRuleResult.Errors, x => SameProperty(x.PropertyName, nameof(CreateOrUpdateAlarmRuleCommand.ComparisonOperator)));
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
    public async Task Runtime_availability_reports_active_alarm_state_unavailable_and_stale_source_windows()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostSampleAsync(client, "DEV-OIL-01", "stopped", new DateTimeOffset(2026, 6, 1, 7, 30, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "state-001");
        await PostAlarmAsync(client, "DEV-OIL-01", "OIL_PRESSURE_LOW", "critical", new DateTimeOffset(2026, 6, 1, 8, 10, 0, TimeSpan.Zero), "alarm-oil-001");

        var response = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeAvailabilityResponse>>(
            "/api/business/v1/iiot/devices/DEV-OIL-01/runtime-availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&freshnessMaxAgeMinutes=15",
            EquipmentRuntimeJson.Options);

        Assert.NotNull(response?.Data);
        Assert.Contains(response.Data.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm && x.SourceType == EquipmentRuntimeSourceType.Alarm);
        Assert.Contains(response.Data.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.StateUnavailable && x.SourceType == EquipmentRuntimeSourceType.DeviceState);
        Assert.Contains(response.Data.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.SourceStale && x.SourceType == EquipmentRuntimeSourceType.StaleSource);
    }

    [Fact]
    public async Task Alarm_rule_endpoint_upserts_and_lists_rule_configuration()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await UpsertAlarmRuleAsync(client, "DEV-OIL-01", "OIL_TEMP_RULE", "temperature", ">=", 90m, true);
        await UpsertAlarmRuleAsync(client, "DEV-OIL-01", "OIL_TEMP_RULE", "temperature", ">=", 95m, false);
        await UpsertAlarmRuleAsync(client, "DEV-OIL-01", " OIL_TEMP_RULE ", "temperature", ">=", 100m, true);

        using var response = await client.GetAsync("/api/business/v1/iiot/alarm-rules?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OIL-01");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        var rule = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("OIL_TEMP_RULE", rule.GetProperty("ruleCode").GetString());
        Assert.Equal("temperature", rule.GetProperty("tagKey").GetString());
        Assert.Equal(100m, rule.GetProperty("thresholdValue").GetDecimal());
        Assert.True(rule.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task List_endpoints_apply_device_status_and_pagination_with_total()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostTagAsync(client, "DEV-PAGE-01", "temperature");
        await PostTagAsync(client, "DEV-PAGE-01", "pressure");
        await PostTagAsync(client, "DEV-PAGE-02", "temperature");
        await UpsertAlarmRuleAsync(client, "DEV-PAGE-01", "PAGE_RULE_01", "temperature", ">=", 90m, true);
        await UpsertAlarmRuleAsync(client, "DEV-PAGE-01", "PAGE_RULE_02", "pressure", ">=", 10m, false);
        await UpsertAlarmRuleAsync(client, "DEV-PAGE-02", "PAGE_RULE_03", "temperature", ">=", 90m, true);
        await PostAlarmAsync(client, "DEV-PAGE-01", "PAGE_ALARM_01", "warning", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), "page-alarm-001");
        await PostAlarmAsync(client, "DEV-PAGE-01", "PAGE_ALARM_02", "critical", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), "page-alarm-002");
        await ClearAlarmAsync(client, "DEV-PAGE-01", "PAGE_ALARM_02", "critical", new DateTimeOffset(2026, 6, 1, 8, 5, 0, TimeSpan.Zero), "page-alarm-002", new DateTimeOffset(2026, 6, 1, 8, 30, 0, TimeSpan.Zero));
        await PostAlarmAsync(client, "DEV-PAGE-02", "PAGE_ALARM_03", "warning", new DateTimeOffset(2026, 6, 1, 8, 10, 0, TimeSpan.Zero), "page-alarm-003");

        using var tagsResponse = await client.GetAsync("/api/business/v1/iiot/tags?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PAGE-01&skip=1&take=1");
        using var rulesResponse = await client.GetAsync("/api/business/v1/iiot/alarm-rules?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PAGE-01&isEnabled=true&skip=0&take=1");
        using var alarmsResponse = await client.GetAsync("/api/business/v1/iiot/alarms?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-PAGE-01&status=raised&skip=0&take=1");

        AssertPagedResponse(await tagsResponse.Content.ReadAsStringAsync(), expectedTotal: 2, expectedItems: 1);
        AssertPagedResponse(await rulesResponse.Content.ReadAsStringAsync(), expectedTotal: 1, expectedItems: 1);
        AssertPagedResponse(await alarmsResponse.Content.ReadAsStringAsync(), expectedTotal: 1, expectedItems: 1);
    }

    [Fact]
    public async Task Oee_endpoint_aggregates_state_samples_for_window()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostSampleAsync(client, "DEV-OEE-01", "running", new DateTimeOffset(2026, 6, 1, 7, 50, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-state-001");
        await PostSampleAsync(client, "DEV-OEE-01", "stopped", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-state-002");

        using var response = await client.GetAsync("/api/business/v1/iiot/oee?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OEE-01&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T10:00:00Z");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("DEV-OEE-01", data.GetProperty("deviceAssetId").GetString());
        Assert.Equal(2, data.GetProperty("stateSampleCount").GetInt32());
        Assert.Equal(0.5m, data.GetProperty("availabilityRate").GetDecimal());
        Assert.Equal(1m, data.GetProperty("performanceRate").GetDecimal());
        Assert.Equal(1m, data.GetProperty("qualityRate").GetDecimal());
        Assert.Equal(0.5m, data.GetProperty("oeeRate").GetDecimal());
        Assert.True(data.GetProperty("performanceRateEstimated").GetBoolean());
        Assert.True(data.GetProperty("qualityRateEstimated").GetBoolean());
    }

    [Fact]
    public async Task Oee_endpoint_keeps_standby_in_runtime_availability_but_out_of_productive_runtime_rate()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostSampleAsync(client, "DEV-OEE-E10", "running", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-e10-001");
        await PostSampleAsync(client, "DEV-OEE-E10", "standby", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-e10-002");

        using var oeeResponse = await client.GetAsync("/api/business/v1/iiot/oee?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OEE-E10&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T10:00:00Z");
        var oeeBody = await oeeResponse.Content.ReadAsStringAsync();
        var availability = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeAvailabilityResponse>>(
            "/api/business/v1/iiot/devices/DEV-OEE-E10/runtime-availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T09:00:00Z&windowEndUtc=2026-06-01T10:00:00Z&freshnessMaxAgeMinutes=120",
            EquipmentRuntimeJson.Options);

        Assert.Equal(HttpStatusCode.OK, oeeResponse.StatusCode);
        using var document = JsonDocument.Parse(oeeBody);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(0.5m, data.GetProperty("availabilityRate").GetDecimal());
        Assert.Equal(0.5m, data.GetProperty("oeeRate").GetDecimal());
        Assert.NotNull(availability?.Data);
        Assert.DoesNotContain(availability.Data.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.StateUnavailable);
    }

    [Fact]
    public async Task Oee_endpoint_uses_loading_time_denominator_and_excludes_planned_down_time()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostSampleAsync(client, "DEV-OEE-LOAD", "running", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-load-001");
        await PostSampleAsync(client, "DEV-OEE-LOAD", "planned_down", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-load-002");
        await PostSampleAsync(client, "DEV-OEE-LOAD", "planned maintenance", new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-load-003");
        await PostSampleAsync(client, "DEV-OEE-LOAD", "stopped", new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-load-004");

        using var response = await client.GetAsync("/api/business/v1/iiot/oee?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OEE-LOAD&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T12:00:00Z");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(4, data.GetProperty("stateSampleCount").GetInt32());
        Assert.Equal(0.5m, data.GetProperty("availabilityRate").GetDecimal());
        Assert.Equal(0.5m, data.GetProperty("loadingRate").GetDecimal());
        Assert.Equal(0.5m, data.GetProperty("oeeRate").GetDecimal());
    }

    [Fact]
    public async Task Oee_endpoint_does_not_treat_productive_as_a_runtime_state_value()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostSampleAsync(client, "DEV-OEE-GHOST", "productive", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-ghost-001");

        using var response = await client.GetAsync("/api/business/v1/iiot/oee?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OEE-GHOST&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T10:00:00Z");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("stateSampleCount").GetInt32());
        Assert.Equal(0m, data.GetProperty("availabilityRate").GetDecimal());
        Assert.Equal(0m, data.GetProperty("oeeRate").GetDecimal());
    }

    [Fact]
    public void Equipment_runtime_contracts_expose_shared_device_state_vocabulary()
    {
        Assert.Equal(EquipmentRuntimeDeviceStateCategory.Productive, EquipmentRuntimeDeviceStates.Classify("RUNNING"));
        Assert.Equal(EquipmentRuntimeDeviceStateCategory.Productive, EquipmentRuntimeDeviceStates.Classify("run"));
        Assert.Equal(EquipmentRuntimeDeviceStateCategory.Productive, EquipmentRuntimeDeviceStates.Classify("运行"));
        Assert.Equal(EquipmentRuntimeDeviceStateCategory.LoadingNonProductive, EquipmentRuntimeDeviceStates.Classify("standby"));
        Assert.Equal(EquipmentRuntimeDeviceStateCategory.LoadingNonProductive, EquipmentRuntimeDeviceStates.Classify("待机"));
        Assert.Equal(EquipmentRuntimeDeviceStateCategory.PlannedDown, EquipmentRuntimeDeviceStates.Classify("planned maintenance"));
        Assert.Equal(EquipmentRuntimeDeviceStateCategory.PlannedDown, EquipmentRuntimeDeviceStates.Classify("计划停机"));
        Assert.Equal(EquipmentRuntimeDeviceStateCategory.Unavailable, EquipmentRuntimeDeviceStates.Classify("faulted"));
        Assert.Equal(EquipmentRuntimeDeviceStateCategory.Unknown, EquipmentRuntimeDeviceStates.Classify("operator-note"));
    }

    [Fact]
    public async Task Oee_and_runtime_availability_use_shared_device_state_vocabulary()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostSampleAsync(client, "DEV-OEE-VOCAB", "运行", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-vocab-001");
        await PostSampleAsync(client, "DEV-OEE-VOCAB", "待机", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-vocab-002");
        await PostSampleAsync(client, "DEV-OEE-VOCAB", "计划停机", new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-vocab-003");
        await PostSampleAsync(client, "DEV-OEE-VOCAB", "operator-note", new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "oee-vocab-004");

        using var oeeResponse = await client.GetAsync("/api/business/v1/iiot/oee?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OEE-VOCAB&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T12:00:00Z");
        var oeeBody = await oeeResponse.Content.ReadAsStringAsync();
        var availability = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeAvailabilityResponse>>(
            "/api/business/v1/iiot/devices/DEV-OEE-VOCAB/runtime-availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T11:00:00Z&windowEndUtc=2026-06-01T12:00:00Z&freshnessMaxAgeMinutes=120",
            EquipmentRuntimeJson.Options);

        Assert.Equal(HttpStatusCode.OK, oeeResponse.StatusCode);
        using var document = JsonDocument.Parse(oeeBody);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(4, data.GetProperty("stateSampleCount").GetInt32());
        Assert.Equal(0.333333m, data.GetProperty("availabilityRate").GetDecimal());
        Assert.Equal(0.75m, data.GetProperty("loadingRate").GetDecimal());
        Assert.Equal(0.333333m, data.GetProperty("oeeRate").GetDecimal());
        Assert.NotNull(availability?.Data);
        Assert.Contains(availability.Data.Items, x =>
            x.ReasonCode == EquipmentRuntimeReasonCodes.StateUnavailable
            && x.SourceType == EquipmentRuntimeSourceType.DeviceState);
    }

    [Fact]
    public async Task Oee_endpoint_marks_performance_quality_estimated_when_state_facts_are_missing()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        using var response = await client.GetAsync("/api/business/v1/iiot/oee?organizationId=org-001&environmentId=env-dev&deviceAssetId=DEV-OEE-NO-DATA&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T10:00:00Z");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("DEV-OEE-NO-DATA", data.GetProperty("deviceAssetId").GetString());
        Assert.Equal(0, data.GetProperty("stateSampleCount").GetInt32());
        Assert.Equal(0m, data.GetProperty("availabilityRate").GetDecimal());
        Assert.Equal(0m, data.GetProperty("performanceRate").GetDecimal());
        Assert.Equal(0m, data.GetProperty("qualityRate").GetDecimal());
        Assert.Equal(0m, data.GetProperty("oeeRate").GetDecimal());
        Assert.True(data.GetProperty("performanceRateEstimated").GetBoolean());
        Assert.True(data.GetProperty("qualityRateEstimated").GetBoolean());
    }

    [Theory]
    [InlineData("/api/business/v1/iiot/runtime-availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&workCenterIds=WC-01")]
    [InlineData("/api/business/v1/iiot/runtime-availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-A&workCenterIds=WC-01")]
    public async Task Batch_runtime_availability_rejects_work_center_scope_in_p0(string requestUri)
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        using var response = await client.GetAsync(requestUri);
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("P0 runtime availability does not support workCenterIds direct query; pass deviceAssetIds.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Batch_runtime_availability_requires_device_scope_in_p0()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        using var response = await client.GetAsync("/api/business/v1/iiot/runtime-availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z");
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("deviceAssetIds is required in P0 runtime availability.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_state_returns_latest_state_freshness_and_active_alarm_summaries()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostSampleAsync(client, "DEV-OIL-01", "running", new DateTimeOffset(2026, 6, 1, 15, 45, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "state-002");
        await PostAlarmAsync(client, "DEV-OIL-01", "OIL_TEMP_HIGH", "warning", new DateTimeOffset(2026, 6, 1, 15, 50, 0, TimeSpan.Zero), "alarm-oil-002");

        var response = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeCurrentStateResponse>>(
            "/api/business/v1/iiot/devices/DEV-OIL-01/current-state?organizationId=org-001&environmentId=env-dev&asOfUtc=2026-06-01T16:00:00Z&freshnessMaxAgeMinutes=30",
            EquipmentRuntimeJson.Options);

        Assert.NotNull(response?.Data);
        Assert.Equal("running", response.Data.CurrentState);
        Assert.True(response.Data.IsSourceFresh);
        var alarm = Assert.Single(response.Data.ActiveAlarms);
        Assert.Equal("OIL_TEMP_HIGH", alarm.AlarmCode);
        Assert.Equal("alarm-oil-002", alarm.ExternalAlarmId);
    }

    [Fact]
    public async Task Current_state_as_of_does_not_return_future_active_alarms()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostSampleAsync(client, "DEV-OIL-04", "running", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "state-004");
        await PostAlarmAsync(client, "DEV-OIL-04", "OIL_TEMP_FUTURE", "warning", new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), "alarm-oil-004");

        var response = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeCurrentStateResponse>>(
            "/api/business/v1/iiot/devices/DEV-OIL-04/current-state?organizationId=org-001&environmentId=env-dev&asOfUtc=2026-06-01T09:30:00Z&freshnessMaxAgeMinutes=60",
            EquipmentRuntimeJson.Options);

        Assert.NotNull(response?.Data);
        Assert.Empty(response.Data.ActiveAlarms);
    }

    [Fact]
    public async Task Current_state_as_of_returns_alarm_cleared_after_as_of()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        await PostSampleAsync(client, "DEV-OIL-06", "running", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), "SCADA-A", "opc-ua-cell-01", "state-006");
        await PostAlarmAsync(client, "DEV-OIL-06", "OIL_TEMP_CLEAR_LATER", "warning", new DateTimeOffset(2026, 6, 1, 9, 10, 0, TimeSpan.Zero), "alarm-oil-006");
        await ClearAlarmAsync(client, "DEV-OIL-06", "OIL_TEMP_CLEAR_LATER", "warning", new DateTimeOffset(2026, 6, 1, 9, 10, 0, TimeSpan.Zero), "alarm-oil-006", new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero));

        var response = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeCurrentStateResponse>>(
            "/api/business/v1/iiot/devices/DEV-OIL-06/current-state?organizationId=org-001&environmentId=env-dev&asOfUtc=2026-06-01T10:00:00Z&freshnessMaxAgeMinutes=60",
            EquipmentRuntimeJson.Options);

        Assert.NotNull(response?.Data);
        var alarm = Assert.Single(response.Data.ActiveAlarms);
        Assert.Equal("alarm-oil-006", alarm.ExternalAlarmId);
    }

    [Fact]
    public async Task Telemetry_sample_idempotency_is_scoped_by_source_system_and_connector()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var occurredAtUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

        var first = await PostSampleAsync(client, "DEV-OIL-02", "running", occurredAtUtc, "SCADA-A", "opc-ua-cell-01", "shared-seq-001");
        var duplicate = await PostSampleAsync(client, "DEV-OIL-02", "running", occurredAtUtc, "SCADA-A", "opc-ua-cell-01", "shared-seq-001");
        var differentConnector = await PostSampleAsync(client, "DEV-OIL-02", "running", occurredAtUtc, "SCADA-A", "opc-ua-cell-02", "shared-seq-001");
        var differentSystem = await PostSampleAsync(client, "DEV-OIL-02", "running", occurredAtUtc, "SCADA-B", "opc-ua-cell-01", "shared-seq-001");

        Assert.Equal(first.TelemetrySummaryId, duplicate.TelemetrySummaryId);
        Assert.Equal(first.DeviceStateSnapshotId, duplicate.DeviceStateSnapshotId);
        Assert.NotEqual(first.TelemetrySummaryId, differentConnector.TelemetrySummaryId);
        Assert.NotEqual(first.DeviceStateSnapshotId, differentConnector.DeviceStateSnapshotId);
        Assert.NotEqual(first.TelemetrySummaryId, differentSystem.TelemetrySummaryId);
        Assert.NotEqual(first.DeviceStateSnapshotId, differentSystem.DeviceStateSnapshotId);
    }

    [Fact]
    public async Task Telemetry_sample_source_sequence_is_trimmed_before_dedupe_lookup()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var occurredAtUtc = new DateTimeOffset(2026, 6, 1, 10, 30, 0, TimeSpan.Zero);

        var first = await PostSampleAsync(client, "DEV-OIL-05", "running", occurredAtUtc, "SCADA-A", "opc-ua-cell-01", "seq-trim-001");
        var duplicate = await PostSampleAsync(client, "DEV-OIL-05", "running", occurredAtUtc, "SCADA-A", "opc-ua-cell-01", " seq-trim-001 ");

        Assert.Equal(first.TelemetrySummaryId, duplicate.TelemetrySummaryId);
        Assert.Equal(first.DeviceStateSnapshotId, duplicate.DeviceStateSnapshotId);
    }

    [Fact]
    public async Task Alarm_idempotency_is_scoped_by_device_alarm_code_and_external_alarm_id()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var raisedAtUtc = new DateTimeOffset(2026, 6, 1, 10, 45, 0, TimeSpan.Zero);

        await PostAlarmAsync(client, "DEV-OIL-07", "OIL_PRESSURE_LOW", "critical", raisedAtUtc, "shared-alarm-ext-001");
        await PostAlarmAsync(client, "DEV-OIL-08", "OIL_PRESSURE_LOW", "critical", raisedAtUtc, "shared-alarm-ext-001");
        await PostAlarmAsync(client, "DEV-OIL-07", "OIL_TEMP_HIGH", "critical", raisedAtUtc, "shared-alarm-ext-001");

        using var response = await client.GetAsync("/api/business/v1/iiot/alarms?organizationId=org-001&environmentId=env-dev&status=raised");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            3,
            document.RootElement.GetProperty("data")
                .GetProperty("items")
                .EnumerateArray()
                .Count(x => x.GetProperty("externalAlarmId").GetString() == "shared-alarm-ext-001"));
    }

    [Fact]
    public async Task Latest_state_uses_occurred_at_before_source_sequence()
    {
        await using var factory = new IndustrialTelemetryLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var olderOccurredAtUtc = new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero);
        var newerOccurredAtUtc = olderOccurredAtUtc.AddMinutes(5);

        await PostSampleAsync(client, "DEV-OIL-03", "running", newerOccurredAtUtc, "SCADA-A", "opc-ua-cell-01", "state-seq-001");
        await PostSampleAsync(client, "DEV-OIL-03", "stopped", olderOccurredAtUtc, "SCADA-A", "opc-ua-cell-01", "state-seq-002");

        var current = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeCurrentStateResponse>>(
            "/api/business/v1/iiot/devices/DEV-OIL-03/current-state?organizationId=org-001&environmentId=env-dev&asOfUtc=2026-06-01T11:05:00Z&freshnessMaxAgeMinutes=30",
            EquipmentRuntimeJson.Options);
        var availability = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeAvailabilityResponse>>(
            "/api/business/v1/iiot/devices/DEV-OIL-03/runtime-availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T11:00:00Z&windowEndUtc=2026-06-01T12:00:00Z&freshnessMaxAgeMinutes=30",
            EquipmentRuntimeJson.Options);

        Assert.NotNull(current?.Data);
        Assert.Equal("running", current.Data.CurrentState);
        Assert.NotNull(availability?.Data);
        Assert.DoesNotContain(availability.Data.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.StateUnavailable);
    }

    [Fact]
    public async Task Runtime_availability_uses_alarm_time_window_when_status_is_inconsistent()
    {
        await using var dbContext = CreateDbContext(nameof(Runtime_availability_uses_alarm_time_window_when_status_is_inconsistent));
        var raisedAtUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-OIL-09", "OIL_TEMP_STUCK", "warning", raisedAtUtc, "alarm-oil-009");
        dbContext.AlarmEvents.Add(alarm);
        await dbContext.SaveChangesAsync();
        dbContext.Entry(alarm).Property(nameof(AlarmEvent.Status)).CurrentValue = "cleared";
        await dbContext.SaveChangesAsync();

        var response = await new QueryRuntimeAvailabilityQueryHandler(dbContext).Handle(
            new QueryRuntimeAvailabilityQuery(
                "org-001",
                "env-dev",
                new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                ["DEV-OIL-09"],
                null),
            CancellationToken.None);

        Assert.Contains(response.Items, x =>
            x.DeviceAssetId == "DEV-OIL-09"
            && x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm
            && x.SourceReferenceId == alarm.Id.ToString());
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

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed record SamplePostResult(string? TelemetrySummaryId, string? DeviceStateSnapshotId);

    private static async Task<SamplePostResult> PostSampleAsync(
        HttpClient client,
        string deviceAssetId,
        string state,
        DateTimeOffset stateOccurredAtUtc,
        string sourceSystem,
        string sourceConnector,
        string sourceSequence)
    {
        using var response = await client.PostAsJsonAsync("/api/business/v1/iiot/samples", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId,
            tagKey = "state",
            bucketStartUtc = stateOccurredAtUtc.AddMinutes(-1),
            bucketEndUtc = stateOccurredAtUtc,
            sampleCount = 1,
            minValue = 1m,
            maxValue = 1m,
            averageValue = 1m,
            sourceSequence,
            sourceSystem,
            sourceConnector,
            deviceState = state,
            stateOccurredAtUtc,
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected sample post to succeed, got {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        return new SamplePostResult(
            data.TryGetProperty("telemetrySummaryId", out var telemetrySummaryId) ? telemetrySummaryId.GetString() : null,
            data.TryGetProperty("deviceStateSnapshotId", out var deviceStateSnapshotId) ? deviceStateSnapshotId.GetString() : null);
    }

    private static async Task PostAlarmAsync(
        HttpClient client,
        string deviceAssetId,
        string alarmCode,
        string severity,
        DateTimeOffset raisedAtUtc,
        string externalAlarmId)
    {
        using var response = await client.PostAsJsonAsync("/api/business/v1/iiot/alarms", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId,
            alarmCode,
            severity,
            raisedAtUtc,
            externalAlarmId,
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected alarm post to succeed, got {(int)response.StatusCode}: {body}");
    }

    private static async Task PostTagAsync(HttpClient client, string deviceAssetId, string tagKey)
    {
        using var response = await client.PostAsJsonAsync("/api/business/v1/iiot/tags", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId,
            tagKey,
            valueType = "number",
            unitCode = "unit",
            samplingPolicy = "sample-10s",
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected tag post to succeed, got {(int)response.StatusCode}: {body}");
    }

    private static void AssertPagedResponse(string body, int expectedTotal, int expectedItems)
    {
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(expectedTotal, data.GetProperty("total").GetInt32());
        Assert.Equal(expectedItems, data.GetProperty("items").GetArrayLength());
    }

    private static async Task UpsertAlarmRuleAsync(
        HttpClient client,
        string deviceAssetId,
        string ruleCode,
        string tagKey,
        string comparisonOperator,
        decimal thresholdValue,
        bool isEnabled)
    {
        using var response = await client.PostAsJsonAsync("/api/business/v1/iiot/alarm-rules", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId,
            ruleCode,
            alarmCode = "OIL_TEMP_HIGH",
            severity = "warning",
            tagKey,
            comparisonOperator,
            thresholdValue,
            unitCode = "celsius",
            isEnabled,
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected alarm rule upsert to succeed, got {(int)response.StatusCode}: {body}");
    }

    private static async Task ClearAlarmAsync(
        HttpClient client,
        string deviceAssetId,
        string alarmCode,
        string severity,
        DateTimeOffset raisedAtUtc,
        string externalAlarmId,
        DateTimeOffset clearedAtUtc)
    {
        using var response = await client.PostAsJsonAsync("/api/business/v1/iiot/alarms", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            deviceAssetId,
            alarmCode,
            severity,
            raisedAtUtc,
            externalAlarmId,
            clearedAtUtc,
            clearedBy = "operator-001",
            clearReason = "test-clear",
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected alarm clear to succeed, got {(int)response.StatusCode}: {body}");
    }

    private sealed class IndustrialTelemetryLiveHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"industrial-telemetry-live-http-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDistributedLock>();
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddInMemoryDistributedLock();
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options
                        .UseInMemoryDatabase(databaseName)
                        .UseInternalServiceProvider(efServices)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                efServices.Dispose();
            }
        }
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
