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
    private static readonly ReferenceDataSeed[] ReferenceData =
    [
        new("material-type", "material", "Material"),
        new("material-type", "service", "Service"),
        new("product-category", "finished-good", "Finished Good"),
        new("product-category", "raw-material", "Raw Material"),
        new("product-category", "packaging", "Packaging"),
        new("product-category", "spare-part", "Spare Part"),
        new("uom-dimension", "mass", "Mass"),
        new("uom-dimension", "quantity", "Quantity"),
        new("uom-dimension", "volume", "Volume"),
        new("uom-dimension", "time", "Time"),
        new("batch-tracking-policy", "none", "No Batch Tracking"),
        new("batch-tracking-policy", "lot", "Lot Tracking"),
        new("serial-tracking-policy", "none", "No Serial Tracking"),
        new("serial-tracking-policy", "serial", "Serial Tracking"),
        new("shelf-life-policy", "none", "No Shelf Life Control"),
        new("shelf-life-policy", "180d", "180 Days"),
        new("shelf-life-policy", "365d", "365 Days"),
        new("storage-condition", "ambient", "Ambient"),
        new("storage-condition", "refrigerated", "Refrigerated"),
        new("storage-condition", "frozen", "Frozen"),
        new("barcode-rule", "ean13", "EAN-13"),
        new("barcode-rule", "code128", "Code 128"),
        new("barcode-rule", "qr", "QR Code"),
        new("partner-type", "supplier", "Supplier"),
        new("partner-type", "customer", "Customer"),
        new("partner-type", "carrier", "Carrier")
    ];

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
        foreach (var item in ReferenceData)
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

    private sealed record ReferenceDataSeed(string CodeSet, string Code, string Name);

    private sealed record UomSeed(string Code, string Name, string DimensionType, int Precision, string RoundingMode);

    private sealed record ShiftSeed(string Code, string Name, TimeOnly StartsAt, TimeOnly EndsAt, int PaidMinutes);
}
