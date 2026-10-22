using System.Globalization;
using System.Text;
using Nerv.IIP.Contracts.Scheduling;
using nietras.SeparatedValues;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public static class SchedulePlanCsv
{
    public static byte[] Export(SchedulePlanContract plan)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\r\n" };
        using var writer = Sep.New(',').Writer(options => options with { Escape = true }).To(text);
        writer.Header.Add(["OrderId", "OperationId", "ResourceId", "StartUtc", "EndUtc", "PlanStatus"]);
        writer.Header.Write();
        foreach (var assignment in plan.Assignments)
        {
            using var row = writer.NewRow();
            row["OrderId"].Set(assignment.OrderId);
            row["OperationId"].Set(assignment.OperationId);
            row["ResourceId"].Set(assignment.ResourceId);
            row["StartUtc"].Set(assignment.StartUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            row["EndUtc"].Set(assignment.EndUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            row["PlanStatus"].Set(plan.Status.ToString());
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }
}
