using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;

namespace Nerv.IIP.Business.ProductEngineering.Domain.Tests;

public sealed class StandardOperationAggregateTests
{
    [Fact]
    public void Standard_operation_captures_default_work_center_control_flags_and_split_minutes()
    {
        var operation = StandardOperation.Create(
            "org-001",
            "env-dev",
            "OP-MIX",
            "混合",
            "WC-MIX-01",
            5,
            30,
            "INHOUSE-QC",
            requiresReporting: true,
            requiresQualityInspection: true,
            isOutsourced: false,
            "标准混合工序");

        Assert.Equal("OP-MIX", operation.OperationCode);
        Assert.Equal("混合", operation.OperationName);
        Assert.Equal("WC-MIX-01", operation.DefaultWorkCenterCode);
        Assert.Equal(5, operation.StandardSetupMinutes);
        Assert.Equal(30, operation.StandardRunMinutes);
        Assert.Equal(35, operation.StandardMinutes);
        Assert.Equal("INHOUSE-QC", operation.ControlKey);
        Assert.True(operation.RequiresReporting);
        Assert.True(operation.RequiresQualityInspection);
        Assert.False(operation.IsOutsourced);
        Assert.True(operation.Enabled);
        Assert.IsType<StandardOperationCreatedDomainEvent>(operation.GetDomainEvents().Single());
    }

    [Fact]
    public void Update_changes_details_without_restoring_archived_state_and_publishes_event()
    {
        var operation = StandardOperation.Create(
            "org-001",
            "env-dev",
            "OP-MIX",
            "混合",
            "WC-MIX-01",
            5,
            30,
            "INHOUSE-QC",
            requiresReporting: true,
            requiresQualityInspection: true,
            isOutsourced: false,
            "标准混合工序");
        operation.ClearDomainEvents();

        operation.Update(
            "自动混合",
            "WC-MIX-AUTO",
            3,
            20,
            "AUTO-QC",
            requiresReporting: false,
            requiresQualityInspection: true,
            isOutsourced: false,
            "自动线标准工序");

        Assert.Equal("自动混合", operation.OperationName);
        Assert.Equal("WC-MIX-AUTO", operation.DefaultWorkCenterCode);
        Assert.Equal(23, operation.StandardMinutes);
        Assert.False(operation.RequiresReporting);
        Assert.True(operation.Enabled);
        Assert.IsType<StandardOperationUpdatedDomainEvent>(operation.GetDomainEvents().Single());
    }

    [Fact]
    public void Archive_disables_standard_operation_without_losing_default_values()
    {
        var operation = StandardOperation.Create(
            "org-001",
            "env-dev",
            "OP-PACK",
            "包装",
            "WC-PACK-01",
            0,
            12,
            "PACK",
            requiresReporting: true,
            requiresQualityInspection: false,
            isOutsourced: false,
            null);
        operation.ClearDomainEvents();

        operation.Archive("replaced by OP-PACK-AUTO");

        Assert.False(operation.Enabled);
        Assert.Equal("WC-PACK-01", operation.DefaultWorkCenterCode);
        Assert.Equal(12, operation.StandardMinutes);
        Assert.IsType<StandardOperationArchivedDomainEvent>(operation.GetDomainEvents().Single());
    }

    [Fact]
    public void Archived_standard_operation_cannot_be_updated_or_archived_again()
    {
        var operation = StandardOperation.Create(
            "org-001",
            "env-dev",
            "OP-PACK",
            "包装",
            "WC-PACK-01",
            0,
            12,
            "PACK",
            requiresReporting: true,
            requiresQualityInspection: false,
            isOutsourced: false,
            null);

        operation.Archive("replaced by OP-PACK-AUTO");

        Assert.Throws<InvalidOperationException>(() => operation.Update(
            "自动包装",
            "WC-PACK-AUTO",
            0,
            8,
            "PACK-AUTO",
            requiresReporting: true,
            requiresQualityInspection: true,
            isOutsourced: false,
            "archived update should fail"));
        Assert.Throws<InvalidOperationException>(() => operation.Archive("duplicate archive"));
        Assert.False(operation.Enabled);
    }
}
