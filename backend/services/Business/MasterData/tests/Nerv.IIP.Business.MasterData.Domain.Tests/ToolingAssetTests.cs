using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Domain.Tests;

public sealed class ToolingAssetTests
{
    [Fact]
    public void Register_WithApplicableScopes_IsAvailableAndApplicable()
    {
        var tooling = ToolingAsset.Register(
            "org-001", "env-dev", "TOOL-001", "Injection mould", "mould",
            ["WC-MOULD"], ["SKU-A", "SKU-B"], 100_000);

        Assert.Equal(ToolingAssetStatus.Available, tooling.Status);
        Assert.True(tooling.IsApplicable("WC-MOULD", "SKU-A"));
        Assert.False(tooling.IsApplicable("WC-OTHER", "SKU-A"));
    }

    [Fact]
    public void RecordUsage_ReachingMaintenanceLife_MarksToolingForMaintenance()
    {
        var tooling = ToolingAsset.Register(
            "org-001", "env-dev", "TOOL-001", "Injection mould", "mould",
            ["WC-MOULD"], ["SKU-A"], 100);

        tooling.RecordUsage(100);

        Assert.Equal(100, tooling.UsageCount);
        Assert.Equal(ToolingAssetStatus.Maintenance, tooling.Status);
        Assert.False(tooling.IsSchedulable);
    }

    [Fact]
    public void Retire_CannotBeReturnedToAvailable()
    {
        var tooling = ToolingAsset.Register(
            "org-001", "env-dev", "TOOL-001", "Injection mould", "mould",
            ["WC-MOULD"], ["SKU-A"], null);

        tooling.ChangeStatus(ToolingAssetStatus.Retired, "end of life");

        Assert.Throws<InvalidOperationException>(() =>
            tooling.ChangeStatus(ToolingAssetStatus.Available, "reuse"));
    }

    [Fact]
    public void ChangeoverMatrix_Create_RequiresPositiveSetupAndTooling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChangeoverMatrixEntry.Create(
            "org-001", "env-dev", "WC-MOULD", "SKU-A", null, "SKU-B", 0, ["TOOL-001"]));
        Assert.Throws<ArgumentException>(() => ChangeoverMatrixEntry.Create(
            "org-001", "env-dev", "WC-MOULD", "SKU-A", null, "SKU-B", 20, []));
    }

    [Fact]
    public void ChangeoverMatrix_MatchesExactSkuBeforeProductFamily()
    {
        var exact = ChangeoverMatrixEntry.Create(
            "org-001", "env-dev", "WC-MOULD", "SKU-A", null, "SKU-B", 20, ["TOOL-001"]);
        var family = ChangeoverMatrixEntry.Create(
            "org-001", "env-dev", "WC-MOULD", null, "FAMILY-A", "SKU-B", 35, ["TOOL-002"]);

        Assert.True(exact.Matches("SKU-A", "FAMILY-A", "SKU-B", "WC-MOULD"));
        Assert.True(family.Matches("SKU-X", "FAMILY-A", "SKU-B", "WC-MOULD"));
        Assert.True(exact.Specificity > family.Specificity);
    }
}
