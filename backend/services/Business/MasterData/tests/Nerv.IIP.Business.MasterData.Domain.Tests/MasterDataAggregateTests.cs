using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
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

        sku.Disable("duplicate registration");

        Assert.True(sku.Disabled);
        Assert.Throws<ArgumentException>(() => sku.Rename(" "));
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
}
