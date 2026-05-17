using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Infrastructure.EntityConfigurations;

internal static class EntityConfigurationJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string SerializeDictionary(Dictionary<string, string> value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    public static Dictionary<string, string> DeserializeDictionary(string value) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(value, JsonOptions) ?? [];

    public static string SerializeCapabilities(List<CapabilityDescriptor> value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    public static List<CapabilityDescriptor> DeserializeCapabilities(string value) =>
        JsonSerializer.Deserialize<List<CapabilityDescriptor>>(value, JsonOptions) ?? [];

    public static readonly ValueComparer<Dictionary<string, string>> DictionaryComparer = new(
        (left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
        value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(StringComparison.Ordinal),
        value => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions) ?? new Dictionary<string, string>());

    public static readonly ValueComparer<List<CapabilityDescriptor>> CapabilitiesComparer = new(
        (left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
        value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(StringComparison.Ordinal),
        value => JsonSerializer.Deserialize<List<CapabilityDescriptor>>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions) ?? new List<CapabilityDescriptor>());
}
