using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
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
        calendar.AddWorkingDay(DayOfWeek.Monday);

        Assert.Equal(480, workCenter.CapacityMinutesPerDay);
        Assert.Single(calendar.WorkingTimes);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkCenter.Create("org-001", "env-dev", "WC-BAD", "Bad Cell", 0));
    }

    [Fact]
    public void Work_calendar_working_times_are_days_only_and_do_not_duplicate_shift_windows()
    {
        var properties = typeof(WorkCalendarWorkingTime)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["DayOfWeek"], properties);
    }

    [Fact]
    public void Work_calendar_deduplicates_recurring_working_days()
    {
        var calendar = WorkCalendar.Create("org-001", "env-dev", "CAL-DAY", "Day Shift Calendar");

        calendar.AddWorkingDay(DayOfWeek.Monday);
        calendar.AddWorkingDay(DayOfWeek.Monday);
        calendar.Update(
            "Day Shift Calendar",
            [
                new WorkCalendarWorkingTime(DayOfWeek.Monday),
                new WorkCalendarWorkingTime(DayOfWeek.Monday),
                new WorkCalendarWorkingTime(DayOfWeek.Tuesday)
            ],
            null,
            null);

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Tuesday], calendar.WorkingTimes.Select(x => x.DayOfWeek).OrderBy(x => x).ToArray());
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
    public void Sku_can_hold_channel_uoms_planning_defaults_and_lifecycle_gates()
    {
        var sku = Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "FG-PLAN-001",
            "Planned Finished Good",
            "EA",
            "finished-good",
            "finished-goods",
            "lot-required",
            "not-serialized",
            "SHELF-24M",
            "ambient",
            "ean13",
            true,
            [],
            inventoryUomCode: "EA",
            purchaseUomCode: "BOX",
            salesUomCode: "CASE",
            manufacturingUomCode: "EA",
            procurementType: "make",
            mrpType: "pd",
            lotSizingPolicy: "fixed-lot",
            minimumLotSize: 10m,
            maximumLotSize: 1000m,
            lotSizeMultiple: 5m,
            safetyStockQuantity: 25m,
            reorderPointQuantity: 50m,
            plannedDeliveryTimeDays: 7,
            inHouseProductionTimeDays: 2,
            goodsReceiptProcessingTimeDays: 1,
            abcClass: "A",
            lifecycleStatus: "blocked",
            purchasingEnabled: false,
            manufacturingEnabled: true,
            salesEnabled: false);

        Assert.Equal("BOX", sku.PurchaseUomCode);
        Assert.Equal("CASE", sku.SalesUomCode);
        Assert.Equal("make", sku.ProcurementType);
        Assert.Equal("pd", sku.MrpType);
        Assert.Equal("fixed-lot", sku.LotSizingPolicy);
        Assert.Equal(25m, sku.SafetyStockQuantity);
        Assert.Equal(50m, sku.ReorderPointQuantity);
        Assert.Equal(7, sku.PlannedDeliveryTimeDays);
        Assert.Equal("A", sku.AbcClass);
        Assert.Equal("blocked", sku.LifecycleStatus);
        Assert.False(sku.PurchasingEnabled);
        Assert.True(sku.ManufacturingEnabled);
        Assert.False(sku.SalesEnabled);
    }

    [Fact]
    public void Sku_rejects_invalid_lifecycle_status()
    {
        Assert.Throws<ArgumentException>(() => Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "FG-BAD-LIFE",
            "Bad Lifecycle",
            "EA",
            "finished-good",
            "finished-goods",
            "lot-required",
            "not-serialized",
            "SHELF-24M",
            "ambient",
            "ean13",
            true,
            [],
            lifecycleStatus: "retired"));
    }

    [Fact]
    public void Business_partner_update_changes_primary_role_and_commercial_defaults()
    {
        var partner = BusinessPartner.Create(
            "org-001",
            "env-dev",
            "BP-001",
            "supplier",
            "Partner",
            ["supplier"],
            "TAX-001",
            taxRegionCode: "CN-SH",
            defaultCurrencyCode: "CNY",
            paymentTermsCode: "NET30",
            primaryAddress: "Shanghai",
            primaryContactName: "Li Wei",
            primaryContactEmail: "li.wei@example.com",
            primaryContactPhone: "+86-21-0000");

        partner.Update(
            "Partner Updated",
            ["customer", "supplier"],
            "TAX-002",
            taxRegionCode: "CN-BJ",
            defaultCurrencyCode: "USD",
            paymentTermsCode: "NET45",
            primaryAddress: "Beijing",
            primaryContactName: "Wang Min",
            primaryContactEmail: "wang.min@example.com",
            primaryContactPhone: "+86-10-0000");

        Assert.Equal("customer", partner.PartnerType);
        Assert.Equal(["customer", "supplier"], partner.PartnerRoles);
        Assert.Equal("CN-BJ", partner.TaxRegionCode);
        Assert.Equal("USD", partner.DefaultCurrencyCode);
        Assert.Equal("NET45", partner.PaymentTermsCode);
        Assert.Equal("Beijing", partner.PrimaryAddress);
        Assert.Equal("Wang Min", partner.PrimaryContactName);
        Assert.Equal("wang.min@example.com", partner.PrimaryContactEmail);
        Assert.Equal("+86-10-0000", partner.PrimaryContactPhone);
    }

    [Fact]
    public void Business_partner_captures_customer_credit_limit_and_currency()
    {
        var partner = BusinessPartner.Create(
            "org-001",
            "env-dev",
            "CUST-001",
            "customer",
            "Credit Customer",
            ["customer"],
            null,
            defaultCurrencyCode: "CNY",
            creditLimit: 1200m,
            creditCurrencyCode: "cny");

        Assert.Equal(1200m, partner.CreditLimit);
        Assert.Equal("CNY", partner.CreditCurrencyCode);

        partner.UpdateCreditLimit(900m, "usd");

        Assert.Equal(900m, partner.CreditLimit);
        Assert.Equal("USD", partner.CreditCurrencyCode);
    }

    [Fact]
    public void Business_partner_rejects_invalid_credit_master_data()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BusinessPartner.Create(
            "org-001",
            "env-dev",
            "CUST-BAD",
            "customer",
            "Bad Credit Customer",
            ["customer"],
            null,
            creditLimit: -1m,
            creditCurrencyCode: "CNY"));

        var missingCurrency = Assert.Throws<ArgumentException>(() => BusinessPartner.Create(
            "org-001",
            "env-dev",
            "CUST-BAD",
            "customer",
            "Bad Credit Customer",
            ["customer"],
            null,
            creditLimit: 100m,
            creditCurrencyCode: " "));
        Assert.Equal("creditCurrencyCode", missingCurrency.ParamName);
        Assert.Contains("Credit limit requires a currency code", missingCurrency.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Business_partner_legacy_update_preserves_existing_roles_when_replacing_primary_role()
    {
        var partner = BusinessPartner.Create(
            "org-001",
            "env-dev",
            "BP-LEGACY",
            "supplier",
            "Partner",
            ["supplier", "carrier"],
            null);

        partner.Update("Partner Renamed", "customer");

        Assert.Equal("customer", partner.PartnerType);
        Assert.Equal(["customer", "carrier"], partner.PartnerRoles);
    }

    [Fact]
    public void Business_partner_primary_role_update_with_details_preserves_secondary_roles()
    {
        var partner = BusinessPartner.Create(
            "org-001",
            "env-dev",
            "BP-DETAIL",
            "supplier",
            "Partner",
            ["supplier", "carrier"],
            "TAX-001");

        partner.ChangePrimaryRole(
            "Partner Renamed",
            "customer",
            "TAX-002",
            taxRegionCode: "CN-BJ",
            defaultCurrencyCode: "CNY",
            paymentTermsCode: "NET30",
            primaryAddress: "Beijing",
            primaryContactName: "Wang Min",
            primaryContactEmail: "wang.min@example.com",
            primaryContactPhone: "+86-10-0000",
            creditLimit: 1000m,
            creditCurrencyCode: "CNY");

        Assert.Equal("customer", partner.PartnerType);
        Assert.Equal(["customer", "carrier"], partner.PartnerRoles);
        Assert.Equal("TAX-002", partner.TaxId);
        Assert.Equal("CN-BJ", partner.TaxRegionCode);
        Assert.Equal("CNY", partner.DefaultCurrencyCode);
        Assert.Equal("NET30", partner.PaymentTermsCode);
        Assert.Equal("Beijing", partner.PrimaryAddress);
        Assert.Equal("Wang Min", partner.PrimaryContactName);
        Assert.Equal("wang.min@example.com", partner.PrimaryContactEmail);
        Assert.Equal("+86-10-0000", partner.PrimaryContactPhone);
        Assert.Equal(1000m, partner.CreditLimit);
        Assert.Equal("CNY", partner.CreditCurrencyCode);
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
            true,
            utilizationRate: 0.85m,
            efficiencyRate: 1.1m,
            numberOfCapacities: 3,
            costCenterCode: "CC-MIX",
            bottleneck: true);
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
        Assert.Equal(0.85m, workCenter.UtilizationRate);
        Assert.Equal(1.1m, workCenter.EfficiencyRate);
        Assert.Equal(3, workCenter.NumberOfCapacities);
        Assert.Equal("CC-MIX", workCenter.CostCenterCode);
        Assert.True(workCenter.Bottleneck);
        Assert.Equal(2019.6m, workCenter.EffectiveCapacityMinutesPerDay);
        Assert.Equal("L", asset.CapacityUomCode);
        Assert.Equal(500m, asset.MinimumCapacity);
        Assert.Equal("MIXER-01", asset.ExternalReferences["scada"]);
    }

    [Fact]
    public void Work_center_validates_capacity_factors_and_can_clear_cost_center()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkCenter.CreateResource("org-001", "env-dev", "WC-UTIL", "Bad Utilization", 480, "work-center", "PLANT", "LINE", workshopCode: null, "CAL", "minute", true, utilizationRate: 1.1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkCenter.CreateResource("org-001", "env-dev", "WC-EFF", "Bad Efficiency", 480, "work-center", "PLANT", "LINE", workshopCode: null, "CAL", "minute", true, efficiencyRate: 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkCenter.CreateResource("org-001", "env-dev", "WC-CAP", "Bad Capacity Count", 480, "work-center", "PLANT", "LINE", workshopCode: null, "CAL", "minute", true, numberOfCapacities: 0));

        var workCenter = WorkCenter.CreateResource(
            "org-001",
            "env-dev",
            "WC-CLEAR",
            "Clear Cost",
            480,
            "work-center",
            "PLANT",
            "LINE",
            workshopCode: null,
            "CAL",
            "minute",
            true,
            costCenterCode: "CC-OLD");

        workCenter.UpdateResource("Clear Cost", 480, "work-center", "PLANT", "LINE", null, "CAL", "minute", true, costCenterCode: "");

        Assert.Null(workCenter.CostCenterCode);
    }

    [Fact]
    public void Uom_calendar_and_shift_capture_effective_window_timezone_and_breaks()
    {
        var conversion = UomConversion.Create(
            "org-001",
            "env-dev",
            "BOX",
            "EA",
            24m,
            0m,
            3,
            "half-up",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31));
        var calendar = WorkCalendar.Create(
            "org-001",
            "env-dev",
            "CAL-SH",
            "Shanghai Calendar",
            "Asia/Shanghai",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            "CN-2026");
        var shift = Shift.Create(
            "org-001",
            "env-dev",
            "SHIFT-DAY",
            "Day Shift",
            new TimeOnly(8, 0),
            new TimeOnly(17, 0),
            480,
            60);

        Assert.Equal(new DateOnly(2026, 12, 31), conversion.EffectiveTo);
        Assert.Equal("Asia/Shanghai", calendar.Timezone);
        Assert.Equal("CN-2026", calendar.HolidayCalendarCode);
        Assert.Equal(new DateOnly(2026, 1, 1), calendar.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), calendar.EffectiveTo);
        Assert.Equal(60, shift.BreakMinutes);
    }

    [Fact]
    public void Shift_rejects_break_minutes_greater_than_paid_minutes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Shift.Create(
            "org-001",
            "env-dev",
            "SHIFT-BAD",
            "Bad Shift",
            new TimeOnly(8, 0),
            new TimeOnly(12, 0),
            120,
            121));
    }

    [Fact]
    public void Disabled_business_partner_rejects_updates()
    {
        var partner = BusinessPartner.Create("org-001", "env-dev", "BP-DISABLED", "supplier", "Disabled Partner");

        partner.Disable("retired");

        Assert.Throws<InvalidOperationException>(() => partner.Update("Renamed", "customer"));
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

    [Fact]
    public void Product_category_is_a_hierarchical_master_data_catalog()
    {
        var root = ProductCategory.Create("org-001", "env-dev", "CAT-FG", "Finished Goods", null, "Sellable output");
        var child = ProductCategory.Create("org-001", "env-dev", "CAT-FG-PUMP", "Pump Products", root.CategoryCode, "Pump family");

        Assert.Equal("CAT-FG", root.CategoryCode);
        Assert.Null(root.ParentCode);
        Assert.Equal("CAT-FG", child.ParentCode);

        child.Update("Pump Products", null, "Promoted to top-level");

        Assert.Null(child.ParentCode);
        Assert.Equal("Promoted to top-level", child.Description);
        Assert.Throws<ArgumentException>(() => child.Update("Pump Products", child.CategoryCode, "self parent"));
    }

    [Fact]
    public void Skill_catalog_captures_group_and_certification_validity()
    {
        var skill = Skill.Create("org-001", "env-dev", "SK-WELD", "Welding", "Manufacturing", true, 24, "Welding certificate");

        Assert.Equal("SK-WELD", skill.SkillCode);
        Assert.Equal("Manufacturing", skill.GroupName);
        Assert.True(skill.RequiresCertification);
        Assert.Equal(24, skill.ValidityMonths);

        skill.Update("Advanced Welding", "Manufacturing", true, 36, "Advanced certificate");

        Assert.Equal("Advanced Welding", skill.SkillName);
        Assert.Equal(36, skill.ValidityMonths);
        Assert.Throws<ArgumentException>(() => Skill.Create("org-001", "env-dev", "SK-BAD", "Bad", "Manufacturing", true, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => skill.Update("Advanced Welding", "Manufacturing", true, 0, null));
    }

    [Fact]
    public void Skill_catalog_clears_validity_when_certification_is_not_required()
    {
        var skill = Skill.Create("org-001", "env-dev", "SK-PACK", "Packing", "Manufacturing", false, 24, null);

        Assert.False(skill.RequiresCertification);
        Assert.Null(skill.ValidityMonths);

        skill.Update("Packing", "Manufacturing", false, 36, null);

        Assert.Null(skill.ValidityMonths);
    }
}
