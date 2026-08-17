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
            ?? throw new KnownException($"未找到计量单位 '{fromUomCode}'。");
        var to = units.SingleOrDefault(x => x.Code == toUomCode)
            ?? throw new KnownException($"未找到计量单位 '{toUomCode}'。");
        if (requireActiveUnits && (from.Disabled || to.Disabled))
        {
            throw new KnownException("计量单位换算要求源计量单位和目标计量单位均已启用。");
        }

        if (!string.Equals(from.DimensionType, to.DimensionType, StringComparison.Ordinal))
        {
            throw new KnownException("计量单位换算要求源计量单位和目标计量单位属于同一量纲。");
        }
    }
}
