using System.Text.Json;
using System.Text.Json.Nodes;
using Nerv.IIP.Contracts.Maintenance;

namespace Nerv.IIP.Contracts.IntegrationEvents.Tests;

public sealed class MaintenanceAssetUnavailableContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string FixedV2Json = """
        {"eventId":"evt-v2-001","eventType":"maintenance.AssetUnavailable","eventVersion":2,"occurredAtUtc":"2026-08-31T01:02:03+00:00","sourceService":"business-maintenance","correlationId":"corr-v2-001","causationId":"cause-v2-001","organizationId":"org-001","environmentId":"env-dev","actor":"system:maintenance","idempotencyKey":"asset-unavailable:WO-001:2026-08-31T01:02:03.0000000\u002B00:00","payload":{"deviceAssetId":"DEVICE-001","reasonCode":"planned-maintenance","fromUtc":"2026-08-31T01:02:03+00:00"}}
        """;
    private const string FixedV2EmptyCausationJson = """
        {"eventId":"evt-v2-001","eventType":"maintenance.AssetUnavailable","eventVersion":2,"occurredAtUtc":"2026-08-31T01:02:03+00:00","sourceService":"business-maintenance","correlationId":"corr-v2-001","causationId":"","organizationId":"org-001","environmentId":"env-dev","actor":"system:maintenance","idempotencyKey":"asset-unavailable:WO-001:2026-08-31T01:02:03.0000000\u002B00:00","payload":{"deviceAssetId":"DEVICE-001","reasonCode":"planned-maintenance","fromUtc":"2026-08-31T01:02:03+00:00"}}
        """;

    public static TheoryData<string> RequiredEnvelopeStringProperties =>
        new()
        {
            "eventId",
            "eventType",
            "sourceService",
            "correlationId",
            "organizationId",
            "environmentId",
            "actor",
            "idempotencyKey",
        };

    public static TheoryData<string, string?> InvalidRequiredEnvelopeStringValues
    {
        get
        {
            var cases = new TheoryData<string, string?>();
            foreach (var propertyName in RequiredEnvelopeStringProperties)
            {
                cases.Add(propertyName, null);
                cases.Add(propertyName, string.Empty);
                cases.Add(propertyName, "   ");
            }

            return cases;
        }
    }

    [Fact]
    public void Asset_unavailable_v2_round_trip_preserves_scope_reason_code_and_canonical_topic()
    {
        var integrationEvent = CreateV2Event();

        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions);
        var topic = AssetUnavailableIntegrationEventTopics.V2(" Production ");

        Assert.NotNull(roundTripped);
        Assert.Equal("org-001", roundTripped.OrganizationId);
        Assert.Equal("env-dev", roundTripped.EnvironmentId);
        Assert.Equal("planned-maintenance", roundTripped.Payload.ReasonCode);
        Assert.Equal(TimeSpan.Zero, roundTripped.OccurredAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, roundTripped.Payload.FromUtc.Offset);
        Assert.Equal(
            "nerv-iip.production.business-maintenance.maintenance.asset-unavailable.v2",
            topic);
    }

    [Fact]
    public void Asset_unavailable_v2_json_matches_the_fixed_wire_contract()
    {
        var json = JsonSerializer.Serialize(CreateV2Event() with { CausationId = string.Empty }, JsonOptions);

        Assert.Equal(FixedV2EmptyCausationJson, json);
        Assert.Contains("\"causationId\":\"\"", json, StringComparison.Ordinal);
        Assert.Contains("\"reasonCode\":\"planned-maintenance\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reason\":", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void Asset_unavailable_v2_rejects_every_other_version_on_read_and_write(int eventVersion)
    {
        var invalid = CreateV2Event() with { EventVersion = eventVersion };
        var json = MutateFixedV2Json(root => root["eventVersion"] = eventVersion);

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
    }

    [Fact]
    public void Asset_unavailable_v2_rejects_missing_event_version_on_read()
    {
        var json = MutateFixedV2Json(root => root.Remove("eventVersion"));

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
    }

    [Theory]
    [InlineData("maintenance.AssetRestored", "business-maintenance")]
    [InlineData("maintenance.AssetUnavailable", "maintenance")]
    public void Asset_unavailable_v2_rejects_another_event_type_or_source(
        string eventType,
        string sourceService)
    {
        var invalid = CreateV2Event() with
        {
            EventType = eventType,
            SourceService = sourceService,
        };

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
    }

    [Theory]
    [InlineData("eventType", "maintenance.AssetRestored")]
    [InlineData("sourceService", "maintenance")]
    public void Asset_unavailable_v2_rejects_another_event_type_or_source_on_read(
        string propertyName,
        string value)
    {
        var json = MutateFixedV2Json(root => root[propertyName] = value);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
    }

    [Theory]
    [MemberData(nameof(RequiredEnvelopeStringProperties))]
    public void Asset_unavailable_v2_rejects_missing_required_envelope_strings_on_read(string propertyName)
    {
        var json = MutateFixedV2Json(root => root.Remove(propertyName));

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
    }

    [Theory]
    [MemberData(nameof(InvalidRequiredEnvelopeStringValues))]
    public void Asset_unavailable_v2_rejects_null_empty_or_whitespace_required_strings_on_read(
        string propertyName,
        string? value)
    {
        var json = MutateFixedV2Json(root => root[propertyName] = value);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
    }

    [Theory]
    [MemberData(nameof(InvalidRequiredEnvelopeStringValues))]
    public void Asset_unavailable_v2_rejects_null_empty_or_whitespace_required_strings_on_write(
        string propertyName,
        string? value)
    {
        var invalid = WithEnvelopeString(CreateV2Event(), propertyName, value);

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Asset_unavailable_v2_allows_explicit_empty_causation_on_read_and_write(string? causationId)
    {
        var integrationEvent = CreateV2Event() with { CausationId = causationId! };
        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(causationId, roundTripped.CausationId);
        Assert.Contains(
            causationId is null ? "\"causationId\":null" : "\"causationId\":\"\"",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Asset_unavailable_v2_rejects_missing_or_whitespace_only_causation_on_read_and_write()
    {
        var missingJson = MutateFixedV2Json(root => root.Remove("causationId"));
        var whitespaceJson = MutateFixedV2Json(root => root["causationId"] = "   ");
        var invalid = CreateV2Event() with { CausationId = "   " };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(missingJson, JsonOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(whitespaceJson, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
    }

    [Fact]
    public void Asset_unavailable_v2_rejects_default_occurred_at_on_read_and_write()
    {
        var json = MutateFixedV2Json(root => root.Remove("occurredAtUtc"));
        var invalid = CreateV2Event() with { OccurredAtUtc = default };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
    }

    [Theory]
    [InlineData("occurredAtUtc")]
    [InlineData("fromUtc")]
    public void Asset_unavailable_v2_rejects_non_utc_fact_times_on_read_and_write(string propertyName)
    {
        var nonUtc = new DateTimeOffset(2026, 8, 31, 9, 2, 3, TimeSpan.FromHours(8));
        var json = MutateFixedV2Json(root =>
        {
            if (propertyName == "occurredAtUtc")
                root[propertyName] = "2026-08-31T09:02:03+08:00";
            else
                root["payload"]![propertyName] = "2026-08-31T09:02:03+08:00";
        });
        var invalid = propertyName == "occurredAtUtc"
            ? CreateV2Event() with { OccurredAtUtc = nonUtc }
            : CreateV2Event() with { Payload = CreateV2Event().Payload with { FromUtc = nonUtc } };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Asset_unavailable_v2_rejects_missing_or_blank_reason_code_on_write(string? reasonCode)
    {
        var invalid = CreateV2Event() with
        {
            Payload = CreateV2Event().Payload with { ReasonCode = reasonCode! },
        };

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Asset_unavailable_v2_rejects_missing_or_blank_reason_code_on_read(string? reasonCode)
    {
        var json = MutateFixedV2Json(root => root["payload"]!["reasonCode"] = reasonCode);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
    }

    [Fact]
    public void Asset_unavailable_v2_rejects_null_payload_on_read_and_write()
    {
        var json = MutateFixedV2Json(root => root["payload"] = null);
        var invalid = CreateV2Event() with { Payload = null! };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Asset_unavailable_v2_rejects_missing_or_blank_device_asset_id_on_read(string? deviceAssetId)
    {
        var json = MutateFixedV2Json(root => root["payload"]!["deviceAssetId"] = deviceAssetId);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Asset_unavailable_v2_rejects_missing_or_blank_device_asset_id_on_write(string? deviceAssetId)
    {
        var invalid = CreateV2Event() with
        {
            Payload = CreateV2Event().Payload with { DeviceAssetId = deviceAssetId! },
        };

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
    }

    [Fact]
    public void Asset_unavailable_v2_rejects_default_from_utc_on_read_and_write()
    {
        var json = MutateFixedV2Json(root => root["payload"]!.AsObject().Remove("fromUtc"));
        var invalid = CreateV2Event() with
        {
            Payload = CreateV2Event().Payload with { FromUtc = default },
        };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(invalid, JsonOptions));
    }

    [Fact]
    public void Asset_unavailable_v2_ignores_unknown_optional_fields()
    {
        const string json = """
            {"eventId":"evt-v2-001","eventType":"maintenance.AssetUnavailable","eventVersion":2,"occurredAtUtc":"2026-08-31T01:02:03Z","sourceService":"business-maintenance","correlationId":"corr-v2-001","causationId":"cause-v2-001","organizationId":"org-001","environmentId":"env-dev","actor":"system:maintenance","idempotencyKey":"idem-v2-001","futureEnvelopeMetadata":{"trace":"future"},"payload":{"deviceAssetId":"DEVICE-001","reasonCode":"planned-maintenance","fromUtc":"2026-08-31T01:02:03Z","futurePayloadMetadata":42}}
            """;

        var roundTripped = JsonSerializer.Deserialize<AssetUnavailableV2IntegrationEvent>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal("planned-maintenance", roundTripped.Payload.ReasonCode);
    }

    [Fact]
    public void Asset_unavailable_v1_json_remains_the_fixed_legacy_wire_contract()
    {
        const string expected = """
            {"eventId":"evt-v1-001","eventType":"maintenance.AssetUnavailable","eventVersion":1,"occurredAtUtc":"2026-08-31T01:02:03+00:00","sourceService":"maintenance","correlationId":"corr-v1-001","causationId":"cause-v1-001","organizationId":"org-001","environmentId":"env-dev","actor":"system:maintenance","idempotencyKey":"idem-v1-001","payload":{"deviceAssetId":"DEVICE-001","reason":"bearing failure","fromUtc":"2026-08-31T01:02:03+00:00"}}
            """;
        var integrationEvent = new AssetUnavailableIntegrationEvent(
            "evt-v1-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-31T01:02:03Z"),
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-v1-001",
            "cause-v1-001",
            "org-001",
            "env-dev",
            "system:maintenance",
            "idem-v1-001",
            new AssetUnavailablePayload(
                "DEVICE-001",
                "bearing failure",
                DateTimeOffset.Parse("2026-08-31T01:02:03Z")));

        var json = JsonSerializer.Serialize(integrationEvent, JsonOptions);

        Assert.Equal(expected, json);
        Assert.DoesNotContain("reasonCode", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Asset_unavailable_versions_bind_to_distinct_exact_topics()
    {
        Assert.Equal(nameof(AssetUnavailableIntegrationEvent), AssetUnavailableIntegrationEventTopics.V1LegacyAlias);
        Assert.Null(AssetUnavailableIntegrationEventTopics.CanonicalSubscriptionTemplate(typeof(AssetUnavailableIntegrationEvent)));
        Assert.Equal(
            AssetUnavailableIntegrationEventTopics.V2Template,
            AssetUnavailableIntegrationEventTopics.CanonicalSubscriptionTemplate(typeof(AssetUnavailableV2IntegrationEvent)));
    }

    [Fact]
    public void Asset_unavailable_v2_topic_build_resolve_and_parse_share_the_canonical_profile_rules()
    {
        var built = AssetUnavailableIntegrationEventTopics.V2(" Production ");
        var resolved = AssetUnavailableIntegrationEventTopics.ResolveSubscriptionTemplate(
            AssetUnavailableIntegrationEventTopics.V2Template,
            " Production ");

        Assert.Equal(built, resolved);
        Assert.True(AssetUnavailableIntegrationEventTopics.TryParseV2(built, out var deploymentProfile));
        Assert.Equal("production", deploymentProfile);
        Assert.False(AssetUnavailableIntegrationEventTopics.TryParseV2(
            "nerv-iip.production.business-maintenance.maintenance.asset-unavailable.v1",
            out _));
        Assert.False(AssetUnavailableIntegrationEventTopics.TryParseV2(
            "nerv-iip.PRODUCTION.business-maintenance.maintenance.asset-unavailable.v2",
            out _));
    }

    [Fact]
    public void Asset_unavailable_v2_topic_rejects_an_envelope_or_topic_from_another_version()
    {
        var integrationEvent = CreateV2Event();
        var topic = AssetUnavailableIntegrationEventTopics.V2("production");

        AssetUnavailableIntegrationEventTopics.EnsureV2EnvelopeMatches(topic, integrationEvent);
        Assert.Throws<ArgumentException>(() =>
            AssetUnavailableIntegrationEventTopics.EnsureV2EnvelopeMatches(
                topic.Replace(".v2", ".v1", StringComparison.Ordinal),
                integrationEvent));
        Assert.Throws<JsonException>(() =>
            AssetUnavailableIntegrationEventTopics.EnsureV2EnvelopeMatches(
                topic,
                integrationEvent with { EventVersion = MaintenanceIntegrationEventVersions.V1 }));
    }

    private static AssetUnavailableV2IntegrationEvent CreateV2Event() =>
        new(
            "evt-v2-001",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V2,
            DateTimeOffset.Parse("2026-08-31T01:02:03Z"),
            MaintenanceIntegrationEventSources.BusinessMaintenance,
            "corr-v2-001",
            "cause-v2-001",
            "org-001",
            "env-dev",
            "system:maintenance",
            "asset-unavailable:WO-001:2026-08-31T01:02:03.0000000+00:00",
            new AssetUnavailableV2Payload(
                "DEVICE-001",
                "planned-maintenance",
                DateTimeOffset.Parse("2026-08-31T01:02:03Z")));

    private static string MutateFixedV2Json(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(FixedV2Json)!.AsObject();
        mutation(root);
        return root.ToJsonString(JsonOptions);
    }

    private static AssetUnavailableV2IntegrationEvent WithEnvelopeString(
        AssetUnavailableV2IntegrationEvent integrationEvent,
        string propertyName,
        string? value) =>
        propertyName switch
        {
            "eventId" => integrationEvent with { EventId = value! },
            "eventType" => integrationEvent with { EventType = value! },
            "sourceService" => integrationEvent with { SourceService = value! },
            "correlationId" => integrationEvent with { CorrelationId = value! },
            "organizationId" => integrationEvent with { OrganizationId = value! },
            "environmentId" => integrationEvent with { EnvironmentId = value! },
            "actor" => integrationEvent with { Actor = value! },
            "idempotencyKey" => integrationEvent with { IdempotencyKey = value! },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName)),
        };
}
