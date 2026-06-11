using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record PersonnelSkillMatrixCell(
    string SkillCode,
    string Level,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo);

public sealed record PersonnelSkillMatrixRow(
    string UserId,
    IReadOnlyCollection<PersonnelSkillMatrixCell> Skills);

public sealed record PersonnelSkillMatrixResponse(
    IReadOnlyCollection<string> SkillCodes,
    IReadOnlyCollection<PersonnelSkillMatrixRow> Rows);

public sealed record ListPersonnelSkillMatrixQuery(
    string OrganizationId,
    string EnvironmentId,
    string? UserId = null,
    string? SkillCode = null,
    bool IncludeDisabled = false) : IQuery<PersonnelSkillMatrixResponse>;

public sealed class ListPersonnelSkillMatrixQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListPersonnelSkillMatrixQuery, PersonnelSkillMatrixResponse>
{
    public async Task<PersonnelSkillMatrixResponse> Handle(ListPersonnelSkillMatrixQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.PersonnelSkills.AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId);
        if (!request.IncludeDisabled)
        {
            query = query.Where(x => !x.Disabled);
        }

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            var userId = request.UserId.Trim();
            query = query.Where(x => x.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(request.SkillCode))
        {
            var skillCode = request.SkillCode.Trim();
            query = query.Where(x => x.SkillCode == skillCode);
        }

        var items = await query
            .OrderBy(x => x.UserId)
            .ThenBy(x => x.SkillCode)
            .ThenByDescending(x => x.EffectiveFrom)
            .Select(x => new
            {
                x.UserId,
                x.SkillCode,
                x.Level,
                x.EffectiveFrom,
                x.EffectiveTo
            })
            .ToListAsync(cancellationToken);

        var skillCodes = items
            .Select(x => x.SkillCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var rows = items
            .GroupBy(x => x.UserId, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new PersonnelSkillMatrixRow(
                group.Key,
                group
                    .Select(x => new PersonnelSkillMatrixCell(x.SkillCode, x.Level, x.EffectiveFrom, x.EffectiveTo))
                    .ToArray()))
            .ToArray();

        return new PersonnelSkillMatrixResponse(skillCodes, rows);
    }
}
