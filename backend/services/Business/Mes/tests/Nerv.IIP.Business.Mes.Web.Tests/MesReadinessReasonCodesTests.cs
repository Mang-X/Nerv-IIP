using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public class MesReadinessReasonCodesTests
{
    [Theory]
    [InlineData("DT-PM")]
    [InlineData("计划保养")]
    public void Planned_maintenance_catalog_code_and_legacy_text_classify_as_maintenance(string reason)
    {
        var classification = MesReadinessReasonCodes.ClassifyEquipmentReason(reason);

        Assert.Equal(EquipmentRuntimeReasonCodes.MaintenanceWindow, classification.Code);
        Assert.Equal("Maintenance", classification.SourceSystem);
    }

    [Theory]
    [InlineData("DT-MECH")]
    [InlineData("DT-ELEC")]
    [InlineData("DT-TOOL")]
    [InlineData("DT-PROC")]
    [InlineData("DT-SETUP")]
    [InlineData("DT-MATERIAL")]
    [InlineData("DT-QUALITY")]
    public void Other_downtime_catalog_codes_classify_as_downtime(string reason)
    {
        var classification = MesReadinessReasonCodes.ClassifyEquipmentReason(reason);

        Assert.Equal(EquipmentRuntimeReasonCodes.Downtime, classification.Code);
        Assert.Equal("BusinessMes", classification.SourceSystem);
    }
}
