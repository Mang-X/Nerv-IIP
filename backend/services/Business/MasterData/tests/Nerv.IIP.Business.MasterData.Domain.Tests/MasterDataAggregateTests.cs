using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using System.Reflection;

namespace Nerv.IIP.Business.MasterData.Domain.Tests;

public sealed class MasterDataAggregateTests
{
    [Fact]
    public void Sku_requires_code_name_unit_and_scope()
    {
        var sku = Sku.Create("org-001", "env-dev", "FG-1000", "Finished Good 1000", "EA", "finished-good");

        Assert.Equal("FG-1000", sku.Code);
        Assert.Equal("EA", sku.Unit);
        Assert.False(sku.Disabled);
    }

    [Fact]
    public void Sku_can_be_disabled_but_not_renamed_to_blank()
    {
        var sku = Sku.Create("org-001", "env-dev", "RM-1000", "Raw Material 1000", "KG", "raw-material");

        Assert.Throws<ArgumentException>(() => sku.Rename(" "));

        sku.Disable("duplicate registration");

        Assert.True(sku.Disabled);
        Assert.Throws<InvalidOperationException>(() => sku.Rename("Raw Material 1000 v2"));
        Assert.Throws<InvalidOperationException>(() => sku.Disable("still duplicate"));
    }

    [Fact]
    public void Business_partner_classifies_customer_supplier_and_carrier()
    {
        var partner = BusinessPartner.Create("org-001", "env-dev", "SUP-001", "supplier", "Acme Supplier");

        Assert.Equal("supplier", partner.PartnerType);
        Assert.False(partner.Disabled);
    }

    [Fact]
    public void Work_center_capacity_and_calendar_are_positive()
    {
        var workCenter = WorkCenter.Create("org-001", "env-dev", "WC-CNC-01", "CNC Cell 01", 480);
        var calendar = WorkCalendar.Create("org-001", "env-dev", "CAL-DAY", "Day Shift Calendar");
        calendar.AddWorkingTime(DayOfWeek.Monday, TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), TimeOnly.FromTimeSpan(TimeSpan.FromHours(16)));

        Assert.Equal(480, workCenter.CapacityMinutesPerDay);
        Assert.Single(calendar.WorkingTimes);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkCenter.Create("org-001", "env-dev", "WC-BAD", "Bad Cell", 0));
    }

    [Fact]
    public void Department_team_and_personnel_skill_reference_business_scope_without_copying_iam_user_facts()
    {
        var department = Department.Create("org-001", "env-dev", "D-PROD", "Production", null);
        var team = Team.Create("org-001", "env-dev", "T-DAY-A", "Day Shift A", department.Code, "day-shift");
        var skill = PersonnelSkill.Assign("org-001", "env-dev", "user-001", "welding", "level-2", DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)));

        Assert.Equal("D-PROD", department.Code);
        Assert.Equal("D-PROD", team.DepartmentCode);
        Assert.Equal("user-001", skill.UserId);
        Assert.Equal("welding", skill.SkillCode);
        Assert.True(skill.IsValidOn(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))));
    }

    [Fact]
    public void Device_asset_belongs_to_work_center_without_holding_control_secrets()
    {
        var asset = DeviceAsset.Register("org-001", "env-dev", "DEV-CNC-01", "CNC-500", "line-1", "WC-CNC-01");
        var secretProperties = typeof(DeviceAsset)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("WC-CNC-01", asset.WorkCenterCode);
        Assert.Empty(secretProperties);
    }

    [Fact]
    public void Unit_of_measure_requires_dimension_precision_and_rounding()
    {
        var uom = UnitOfMeasure.Create("org-001", "env-dev", "KG", "Kilogram", "mass", 3, "half-up");

        Assert.Equal("KG", uom.Code);
        Assert.Equal("mass", uom.DimensionType);
        Assert.Equal(3, uom.Precision);
        Assert.Equal("half-up", uom.RoundingMode);
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitOfMeasure.Create("org-001", "env-dev", "BAD", "Bad", "mass", -1, "half-up"));
    }

    [Fact]
    public void Uom_conversion_rejects_same_unit_and_non_positive_factor()
    {
        var conversion = UomConversion.Create("org-001", "env-dev", "KG", "G", 1000m, 0m, 3, "half-up", DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.Equal("KG", conversion.FromUomCode);
        Assert.Equal("G", conversion.ToUomCode);
        Assert.Equal(1000m, conversion.Factor);
        Assert.Throws<ArgumentException>(() => UomConversion.Create("org-001", "env-dev", "KG", "KG", 1m, 0m, 3, "half-up", DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.Throws<ArgumentOutOfRangeException>(() => UomConversion.Create("org-001", "env-dev", "KG", "G", 0m, 0m, 3, "half-up", DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [Fact]
    public void Sku_can_hold_industrial_material_policy()
    {
        var sku = Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "RM-API-001",
            "Active Ingredient",
            "KG",
            "raw-material",
            "raw-material",
            "lot-required",
            "not-serialized",
            "SHELF-24M",
            "COLD",
            "BARCODE-RAW",
            true,
            ["hazardous", "gmp-controlled"]);

        Assert.Equal("KG", sku.BaseUomCode);
        Assert.Equal("lot-required", sku.BatchTrackingPolicy);
        Assert.Equal("SHELF-24M", sku.ShelfLifePolicyCode);
        Assert.True(sku.QualityRequired);
        Assert.Contains("hazardous", sku.ComplianceTags);
    }

    [Fact]
    public void Resource_hierarchy_shift_and_reference_data_are_business_master_facts()
    {
        var site = Site.Create("org-001", "env-dev", "SITE-SH", "Shanghai Site", "Asia/Shanghai");
        var workshop = Workshop.Create("org-001", "env-dev", "WS-MIX", "Mixing Workshop", site.Code, "user-manager", "Process area");
        var line = ProductionLine.Create("org-001", "env-dev", "LINE-MIX-01", "Mixing Line 01", site.Code, workshop.Code);
        var shift = Shift.Create("org-001", "env-dev", "SHIFT-NIGHT", "Night Shift", TimeOnly.FromTimeSpan(TimeSpan.FromHours(22)), TimeOnly.FromTimeSpan(TimeSpan.FromHours(6)), 420);
        var code = ReferenceDataCode.Create("org-001", "env-dev", "material-form", "powder", "Powder");

        Assert.Equal("SITE-SH", workshop.SiteCode);
        Assert.Equal("user-manager", workshop.ManagerUserId);
        Assert.Equal("SITE-SH", line.SiteCode);
        Assert.Equal("WS-MIX", line.WorkshopCode);
        Assert.True(shift.CrossesMidnight);
        Assert.Equal("material-form", code.CodeSet);
        Assert.Equal("powder", code.Code);
    }

    [Fact]
    public void Work_center_and_device_asset_capture_static_resource_capability()
    {
        var workCenter = WorkCenter.CreateResource(
            "org-001",
            "env-dev",
            "WC-MIX-01",
            "Mixing Vessel 01",
            720,
            "process-unit",
            "PLANT-01",
            "LINE-MIX-01",
            "WS-MIX",
            "CAL-24X5",
            "L",
            true);
        var asset = DeviceAsset.RegisterCapability(
            "org-001",
            "env-dev",
            "DEV-MIX-01",
            "Mixing Vessel",
            "LINE-MIX-01",
            "WC-MIX-01",
            "vessel",
            "Acme",
            "SN-001",
            500m,
            2000m,
            "L",
            "high",
            true,
            true,
            new Dictionary<string, string> { ["scada"] = "MIXER-01" });

        Assert.Equal("process-unit", workCenter.ResourceType);
        Assert.Equal("PLANT-01", workCenter.PlantCode);
        Assert.Equal("WS-MIX", workCenter.WorkshopCode);
        Assert.Equal("L", asset.CapacityUomCode);
        Assert.Equal(500m, asset.MinimumCapacity);
        Assert.Equal("MIXER-01", asset.ExternalReferences["scada"]);
    }

    [Fact]
    public void Team_member_tracks_user_membership_leadership_and_remove_lifecycle()
    {
        var member = TeamMember.Assign(
            "org-001",
            "env-dev",
            "TEAM-A",
            "user-worker-001",
            true,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 12, 31));

        Assert.Equal("TEAM-A", member.TeamCode);
        Assert.Equal("user-worker-001", member.UserId);
        Assert.True(member.IsLeader);
        Assert.True(member.IsEffectiveOn(new DateOnly(2026, 7, 1)));

        member.Remove("transferred");

        Assert.True(member.Disabled);
        Assert.False(member.IsEffectiveOn(new DateOnly(2026, 7, 1)));
        Assert.Throws<InvalidOperationException>(() => member.Remove("again"));
    }
}
