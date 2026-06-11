using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

internal static class UomConversionValidator
{
    public static async Task ValidateUnitsAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string fromUomCode,
        string toUomCode,
        bool requireActiveUnits,
        CancellationToken cancellationToken)
    {
        var units = await dbContext.UnitsOfMeasure
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                (x.Code == fromUomCode || x.Code == toUomCode))
            .Select(x => new { x.Code, x.DimensionType, x.Disabled })
            .ToListAsync(cancellationToken);
        var from = units.SingleOrDefault(x => x.Code == fromUomCode)
            ?? throw new KnownException($"Unit of measure '{fromUomCode}' was not found.");
        var to = units.SingleOrDefault(x => x.Code == toUomCode)
            ?? throw new KnownException($"Unit of measure '{toUomCode}' was not found.");
        if (requireActiveUnits && (from.Disabled || to.Disabled))
        {
            throw new KnownException("UOM conversion requires active units of measure.");
        }

        if (!string.Equals(from.DimensionType, to.DimensionType, StringComparison.Ordinal))
        {
            throw new KnownException("UOM conversion requires source and target units in the same dimension.");
        }
    }
}
