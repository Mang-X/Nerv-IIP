using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.MasterData;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public static class SchedulingTimeContextQuery
{
    public static async Task<IReadOnlyCollection<SchedulingTimeContextItem>> ResolveAsync(
        ApplicationDbContext db, SchedulingTimeContextRequest request, CancellationToken ct)
    {
        var centers = await db.WorkCenters.AsNoTracking().Where(x => x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId && request.WorkCenterCodes.Contains(x.Code))
            .Select(x => new { x.Code, x.PlantCode }).ToArrayAsync(ct);
        var plantCodes = centers.Select(x => x.PlantCode).Distinct().ToArray();
        var sites = await db.Sites.AsNoTracking().Where(x => x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId && plantCodes.Contains(x.Code))
            .Select(x => new { x.Code, x.Name, x.Timezone }).ToDictionaryAsync(x => x.Code, ct);
        return request.WorkCenterCodes.Select(code =>
        {
            var center = centers.SingleOrDefault(x => x.Code == code);
            if (center is null) return new SchedulingTimeContextItem(code, null, null, null, "工作中心不存在");
            if (!sites.TryGetValue(center.PlantCode, out var site))
                return new SchedulingTimeContextItem(code, center.PlantCode, null, null, "工作中心未关联有效工厂");
            return new SchedulingTimeContextItem(code, site.Code, site.Name, site.Timezone,
                string.IsNullOrWhiteSpace(site.Timezone) ? "工厂未配置时区" : null);
        }).ToArray();
    }
}
