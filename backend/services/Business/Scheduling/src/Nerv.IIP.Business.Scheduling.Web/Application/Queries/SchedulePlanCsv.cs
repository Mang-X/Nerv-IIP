using System.Globalization;
using System.Text;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public static class SchedulePlanCsv
{
    public static byte[] Export(SchedulePlanContract plan)
    {
        var csv = new StringBuilder("OrderId,OperationId,ResourceId,StartUtc,EndUtc,PlanStatus\r\n");
        foreach (var assignment in plan.Assignments)
        {
            csv.AppendJoin(',', new[]
            {
                Escape(assignment.OrderId),
                Escape(assignment.OperationId),
                Escape(assignment.ResourceId),
                assignment.StartUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                assignment.EndUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                plan.Status.ToString(),
            });
            csv.Append("\r\n");
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
