using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Contracts.Coding;
using System.Text.Json;

namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

public sealed class MasterDataSeedService(ApplicationDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly UomSeed[] Units =
    [
        new("kg", "千克", "weight", 3, "half-up"),
        new("g", "克", "weight", 3, "half-up"),
        new("pcs", "件", "count", 0, "half-up"),
        new("l", "升", "volume", 3, "half-up"),
        new("min", "分钟", "time", 0, "half-up")
    ];

    private static readonly ShiftSeed[] Shifts =
    [
        new("DAY", "Day Shift", new TimeOnly(8, 0), new TimeOnly(20, 0), 720),
        new("NIGHT", "Night Shift", new TimeOnly(20, 0), new TimeOnly(8, 0), 720)
    ];

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        foreach (var rule in StandardCodeRules.All)
        {
            var segmentsJson = JsonSerializer.Serialize(rule.Segments, JsonOptions);
            var existing = await dbContext.CodeRules.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.RuleKey == rule.RuleKey,
                cancellationToken);
            if (existing is null)
            {
                dbContext.CodeRules.Add(CodeRule.Create(
                    organizationId,
                    environmentId,
                    rule.RuleKey,
                    rule.DisplayName,
                    rule.AppliesTo,
                    (int)rule.Scope,
                    segmentsJson,
                    rule.IsActive,
                    rule.Version));
            }
            else
            {
                existing.ReplaceDefinition(
                    rule.DisplayName,
                    rule.AppliesTo,
                    (int)rule.Scope,
                    segmentsJson,
                    rule.IsActive,
                    rule.Version);
            }

            if (!await dbContext.CodeRuleVersions.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.RuleKey == rule.RuleKey &&
                    x.Version == rule.Version,
                    cancellationToken))
            {
                dbContext.CodeRuleVersions.Add(CodeRuleVersion.Record(
                    organizationId,
                    environmentId,
                    rule.RuleKey,
                    rule.DisplayName,
                    rule.AppliesTo,
                    (int)rule.Scope,
                    segmentsJson,
                    rule.IsActive,
                    rule.Version,
                    CodeRuleVersionStatus.Active,
                    DateTimeOffset.UnixEpoch,
                    "standard-seed",
                    "standard code rule seed",
                    DateTimeOffset.UtcNow));
            }
        }

        foreach (var item in MasterDataDictionaryRules.StandardReferenceData)
        {
            var existing = await dbContext.ReferenceDataCodes.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.CodeSet == item.CodeSet &&
                x.Code == item.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create(
                    organizationId,
                    environmentId,
                    item.CodeSet,
                    item.Code,
                    item.Name));
            }
            else if (!existing.Disabled && !string.Equals(existing.Name, item.Name, StringComparison.Ordinal))
            {
                existing.Update(item.Name);
            }
        }

        var obsoleteCodeSets = MasterDataDictionaryRules.ObsoleteSeedCodes.Keys.ToArray();
        var obsoleteReferenceData = await dbContext.ReferenceDataCodes
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                obsoleteCodeSets.Contains(x.CodeSet) &&
                !x.Disabled)
            .ToArrayAsync(cancellationToken);
        foreach (var item in obsoleteReferenceData.Where(item =>
            MasterDataDictionaryRules.ObsoleteSeedCodes[item.CodeSet].Contains(item.Code)))
        {
            item.Disable("disabled by master-data dictionary rules seed");
        }

        foreach (var item in Units)
        {
            var existing = await dbContext.UnitsOfMeasure.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Code == item.Code,
                cancellationToken);
            if (existing is null)
            {
                dbContext.UnitsOfMeasure.Add(UnitOfMeasure.Create(
                    organizationId,
                    environmentId,
                    item.Code,
                    item.Name,
                    item.DimensionType,
                    item.Precision,
                    item.RoundingMode));
            }
            else if (!existing.Disabled &&
                (!string.Equals(existing.Name, item.Name, StringComparison.Ordinal) ||
                 !string.Equals(existing.DimensionType, item.DimensionType, StringComparison.Ordinal) ||
                 existing.Precision != item.Precision ||
                 !string.Equals(existing.RoundingMode, item.RoundingMode, StringComparison.Ordinal)))
            {
                existing.Update(item.Name, item.DimensionType, item.Precision, item.RoundingMode);
            }
        }

        if (!await dbContext.UomConversions.AnyAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.FromUomCode == "kg" &&
                x.ToUomCode == "g" &&
                x.EffectiveFrom == new DateOnly(2026, 1, 1),
                cancellationToken))
        {
            dbContext.UomConversions.Add(UomConversion.Create(
                organizationId,
                environmentId,
                "kg",
                "g",
                1000m,
                0m,
                3,
                "half-up",
                new DateOnly(2026, 1, 1)));
        }

        foreach (var item in Shifts)
        {
            if (!await dbContext.Shifts.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.Code == item.Code,
                    cancellationToken))
            {
                dbContext.Shifts.Add(Shift.Create(
                    organizationId,
                    environmentId,
                    item.Code,
                    item.Name,
                    item.StartsAt,
                    item.EndsAt,
                    item.PaidMinutes));
            }
        }

        if (!await dbContext.WorkCalendars.AnyAsync(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Code == "STANDARD",
                cancellationToken))
        {
            var calendar = WorkCalendar.Create(organizationId, environmentId, "STANDARD", "Standard Calendar");
            foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            {
                calendar.AddWorkingDay(day);
            }

            dbContext.WorkCalendars.Add(calendar);
        }

        await SeedLeaderDemoFactsAsync(organizationId, environmentId, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedLeaderDemoFactsAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var site = await dbContext.Sites.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "SITE-001", cancellationToken);
        if (site is null)
        {
            dbContext.Sites.Add(Site.Create(organizationId, environmentId, "SITE-001", "Leader Demo Site", "Asia/Shanghai"));
        }
        else if (site.Name != "Leader Demo Site" || site.Timezone != "Asia/Shanghai" || site.Disabled)
        {
            throw Collision("SITE-001");
        }

        var line = await dbContext.ProductionLines.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "LINE-DEMO-01", cancellationToken);
        if (line is null)
        {
            dbContext.ProductionLines.Add(ProductionLine.Create(organizationId, environmentId, "LINE-DEMO-01", "Leader Demo Line", "SITE-001"));
        }
        else if (line.Name != "Leader Demo Line" || line.SiteCode != "SITE-001" || line.WorkshopCode is not null || line.Disabled)
        {
            throw Collision("LINE-DEMO-01");
        }

        var workCenter = await dbContext.WorkCenters.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "WC-CNC-DEMO", cancellationToken);
        if (workCenter is null)
        {
            dbContext.WorkCenters.Add(WorkCenter.CreateResource(
                organizationId, environmentId, "WC-CNC-DEMO", "Leader Demo CNC Work Center", 480, "work-center",
                "SITE-001", "LINE-DEMO-01", "STANDARD", "minute", true));
        }
        else if (workCenter.Name != "Leader Demo CNC Work Center" || workCenter.CapacityMinutesPerDay != 480 ||
                 workCenter.PlantCode != "SITE-001" || workCenter.LineCode != "LINE-DEMO-01" || workCenter.Disabled)
        {
            throw Collision("WC-CNC-DEMO");
        }

        await SeedSkuAsync(organizationId, environmentId, "SKU-DEMO-001", "Leader Demo Finished Product", "finished-goods", cancellationToken);
        await SeedSkuAsync(organizationId, environmentId, "SKU-DEMO-RM-001", "Leader Demo Raw Material", "raw-material", cancellationToken);

        var customer = await dbContext.BusinessPartners.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "CUST-DEMO-001", cancellationToken);
        if (customer is null)
        {
            dbContext.BusinessPartners.Add(BusinessPartner.Create(organizationId, environmentId, "CUST-DEMO-001", "customer", "Leader Demo Customer"));
        }
        else if (customer.Name != "Leader Demo Customer" || customer.PartnerType != "customer" || customer.Disabled)
        {
            throw Collision("CUST-DEMO-001");
        }

        var device = await dbContext.DeviceAssets.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == "DEV-CNC-DEMO", cancellationToken);
        if (device is null)
        {
            dbContext.DeviceAssets.Add(DeviceAsset.RegisterCapability(
                organizationId, environmentId, "DEV-CNC-DEMO", "Leader Demo CNC", "LINE-DEMO-01", "WC-CNC-DEMO",
                "cnc", "", "", null, null, "", "high", true, true, new Dictionary<string, string>()));
        }
        else if (device.Model != "Leader Demo CNC" || device.LineCode != "LINE-DEMO-01" || device.WorkCenterCode != "WC-CNC-DEMO" ||
                 !device.Maintainable || !device.TelemetryEnabled || device.Disabled)
        {
            throw Collision("DEV-CNC-DEMO");
        }
    }

    private async Task SeedSkuAsync(string organizationId, string environmentId, string code, string name, string category, CancellationToken cancellationToken)
    {
        var sku = await dbContext.Skus.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code, cancellationToken);
        if (sku is null)
        {
            dbContext.Skus.Add(Sku.Create(organizationId, environmentId, code, name, "pcs", category));
        }
        else if (sku.Name != name || sku.Unit != "pcs" || sku.Category != category || sku.Disabled)
        {
            throw Collision(code);
        }
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved leader-demo master-data fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");

    private sealed record UomSeed(string Code, string Name, string DimensionType, int Precision, string RoundingMode);

    private sealed record ShiftSeed(string Code, string Name, TimeOnly StartsAt, TimeOnly EndsAt, int PaidMinutes);
}
