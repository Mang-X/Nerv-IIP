using System.Reflection;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Domain.Tests;

public sealed class InventoryVocabularyContractTests
{
    [Fact]
    public void Inventory_vocabulary_matches_the_1891_reference_data_and_public_contract()
    {
        var expectedMovementTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Inbound"] = "inbound",
            ["Outbound"] = "outbound",
            ["Transfer"] = "transfer",
            ["Adjustment"] = "adjustment",
            ["CountAdjustment"] = "count-adjustment",
            ["StatusTransferOut"] = "status-transfer-out",
            ["StatusTransferIn"] = "status-transfer-in",
        };
        var expectedQualityStatuses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Unrestricted"] = "unrestricted",
            ["Quality"] = "quality",
            ["Restricted"] = "restricted",
            ["Blocked"] = "blocked",
        };

        Assert.Equal(expectedMovementTypes, PublicStringConstantsOf(typeof(InventoryMovementTypes)));
        Assert.Equal(expectedQualityStatuses, PublicStringConstantsOf(typeof(InventoryQualityStatuses)));
    }

    private static IReadOnlyDictionary<string, string> PublicStringConstantsOf(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
    }
}
