using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

internal sealed record WorkOrderTaskReadScope(string? AssignedUserIds, string? TeamIds, string? WorkCenterIds)
{
    public bool IsRestricted => AssignedUserIds is not null || TeamIds is not null || WorkCenterIds is not null;

    public IQueryable<OperationTask> ApplyTo(IQueryable<OperationTask> tasks)
    {
        if (!IsRestricted)
        {
            return tasks;
        }

        var users = SplitCsv(AssignedUserIds);
        var teams = SplitCsv(TeamIds);
        var centers = SplitCsv(WorkCenterIds);
        return tasks.Where(task =>
            users.Contains(task.AssignedUserId) || teams.Contains(task.TeamId) || centers.Contains(task.WorkCenterId));
    }

    internal static IReadOnlyCollection<string> SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
    }
}
