using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Seed;

public sealed class MasterDataSeedService(ApplicationDbContext dbContext)
{
    private static readonly UomSeed[] Units =
    [
        new("kg", "Kilogram", "mass", 3, "half-up"),
        new("g", "Gram", "mass", 3, "half-up"),
        new("pcs", "Piece", "quantity", 0, "half-up"),
        new("l", "Liter", "volume", 3, "half-up"),
        new("min", "Minute", "time", 0, "half-up")
    ];

    private static readonly ShiftSeed[] Shifts =
    [
        new("DAY", "Day Shift", new TimeOnly(8, 0), new TimeOnly(20, 0), 720),
        new("NIGHT", "Night Shift", new TimeOnly(20, 0), new TimeOnly(8, 0), 720)
    ];

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        foreach (var item in MasterDataDictionaryRules.StandardReferenceData)
        {
            if (!await dbContext.ReferenceDataCodes.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.CodeSet == item.CodeSet &&
                    x.Code == item.Code,
                    cancellationToken))
            {
                dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create(
                    organizationId,
                    environmentId,
                    item.CodeSet,
                    item.Code,
                    item.Name));
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
            if (!await dbContext.UnitsOfMeasure.AnyAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.Code == item.Code,
                    cancellationToken))
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
                calendar.AddWorkingTime(day, new TimeOnly(8, 0), new TimeOnly(17, 0));
            }

            dbContext.WorkCalendars.Add(calendar);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record UomSeed(string Code, string Name, string DimensionType, int Precision, string RoundingMode);

    private sealed record ShiftSeed(string Code, string Name, TimeOnly StartsAt, TimeOnly EndsAt, int PaidMinutes);
}
