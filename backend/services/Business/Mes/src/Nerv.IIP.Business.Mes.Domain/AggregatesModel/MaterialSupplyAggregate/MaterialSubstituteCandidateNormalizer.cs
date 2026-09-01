namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

public static class MaterialSubstituteCandidateNormalizer
{
    public static string[] Normalize(string materialId, IEnumerable<string> substituteMaterialIds)
    {
        ArgumentNullException.ThrowIfNull(substituteMaterialIds);
        var normalizedMaterialId = DomainGuard.Required(materialId, nameof(materialId));
        return substituteMaterialIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => !string.Equals(x, normalizedMaterialId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
