using System.Text.Json;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Contracts.IntegrationEvents.Tests;

public sealed class DefectRaisedContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string LegacyPayload = """
        {"defectNo":"DEF-001","workOrderId":"WO-001","operationTaskId":null,
         "defectCode":"SURFACE","quantity":2,"recordedAtUtc":"2026-10-12T01:00:00Z"}
        """;

    [Fact]
    public void Legacy_defect_payload_without_product_remains_readable()
    {
        var payload = JsonSerializer.Deserialize<DefectRaisedPayload>(LegacyPayload, JsonOptions)!;
        Assert.Equal(("DEF-001", "WO-001", null, "SURFACE", 2m),
            (payload.DefectNo, payload.WorkOrderId, payload.OperationTaskId, payload.DefectCode, payload.Quantity));
        var json = JsonSerializer.SerializeToElement(payload, JsonOptions);
        Assert.True(json.TryGetProperty("skuCode", out var product));
        Assert.Equal(JsonValueKind.Null, product.ValueKind);
    }

    [Fact]
    public void Product_extension_survives_round_trip_and_is_ignored_by_legacy_payload_reader()
    {
        var newJson = LegacyPayload.Replace("\"defectNo\"", "\"skuCode\":\"SKU-B\",\"defectNo\"", StringComparison.Ordinal);
        var current = JsonSerializer.Deserialize<DefectRaisedPayload>(newJson, JsonOptions)!;
        var roundTripJson = JsonSerializer.Serialize(current, JsonOptions);
        using var roundTrip = JsonDocument.Parse(roundTripJson);
        Assert.Equal("SKU-B", roundTrip.RootElement.GetProperty("skuCode").GetString());
        var legacy = JsonSerializer.Deserialize<LegacyDefectPayload>(roundTripJson, JsonOptions)!;
        Assert.Equal(new LegacyDefectPayload("DEF-001", "WO-001", null, "SURFACE", 2m,
            DateTimeOffset.Parse("2026-10-12T01:00:00Z")), legacy);
    }

    private sealed record LegacyDefectPayload(string DefectNo, string WorkOrderId, string? OperationTaskId,
        string DefectCode, decimal Quantity, DateTimeOffset RecordedAtUtc);
}
