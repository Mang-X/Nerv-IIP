using System.Diagnostics;
using Nerv.IIP.Business.Performance.Tests;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// DomainInvariant：ADR 0022 §14、ADR 0032 §6；复用既有规模输入，纯计算对等比较，不声称 PostgreSQL/FullStack。
public sealed class ResourceTransferScaleTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("demo", 100)]
    [InlineData("medium", 500)]
    [InlineData("stress", 1000)]
    public void Local_candidate_median_is_at_most_half_of_equivalent_full_recalculation(string name, int orderCount)
    {
        var problem = SchedulingScaleProblemFactory.Create(new(name, orderCount));
        var scheduler = new FiniteCapacityScheduler();
        var baseline = scheduler.Schedule(problem, "baseline", problem.HorizonStartUtc);
        // 每档取同一形状：最后开始的资源工序停机半小时；只影响该尾部资源/后继局部链路。
        var target = baseline.Assignments.OrderByDescending(x => x.StartUtc).ThenBy(x => x.OrderId, StringComparer.Ordinal).First();
        var deviation = new SchedulingResourceUnavailableDeviation("benchmark/outage", "v1", target.StartUtc,
            "downtime", target.ResourceId, target.StartUtc, target.StartUtc.AddMinutes(30));
        var input = new ReschedulingCandidateInput(problem, baseline, [deviation], [], [],
            new(problem.HorizonStartUtc, TimeSpan.Zero, new Dictionary<string, TimeSpan>()));
        var fullProblem = problem with
        {
            // 全量对照应用相同转移资格：目标工序排除原设备，不能把等待原设备当成转移对照。
            Orders = problem.Orders.Select(order => order.OrderId == target.OrderId ? order with
            {
                Operations = order.Operations.Select(operation => operation.OperationId == target.OperationId ? operation with
                { EligibleResourceIds = operation.EligibleResourceIds.Where(id => id != target.ResourceId).ToArray() } : operation).ToArray()
            } : order).ToArray(),
            UnavailabilityWindows = problem.UnavailabilityWindows.Append(new(target.ResourceId, null, deviation.StartUtc, deviation.EndUtc, deviation.ReasonCode)).ToArray()
        };
        // 同进程、同档位、同源输入与停机，入口包含各自规范化/指纹/输出；预热后交替取三次中位数。
        var candidate = ResourceTransferCandidateGenerator.Generate(input);
        scheduler.Schedule(fullProblem, "full", problem.HorizonStartUtc);
        var local = new List<double>();
        var full = new List<double>();
        for (var index = 0; index < 3; index++)
        {
            var clock = Stopwatch.StartNew();
            candidate = ResourceTransferCandidateGenerator.Generate(input);
            clock.Stop();
            local.Add(clock.Elapsed.TotalMilliseconds);
            clock.Restart();
            scheduler.Schedule(fullProblem, "full", problem.HorizonStartUtc);
            clock.Stop();
            full.Add(clock.Elapsed.TotalMilliseconds);
        }
        var localMedian = local.Order().ElementAt(1);
        var fullMedian = full.Order().ElementAt(1);
        output.WriteLine($"profile={name}; orders={orderCount}; operations={orderCount * 4}; resources=24; affected={candidate.Impact.AffectedOperations.Count}; movable={candidate.Impact.RecalculateAssignments.Count}; candidates=1; localMs=[{string.Join(',', local)}]; fullMs=[{string.Join(',', full)}]; localMedianMs={localMedian}; fullMedianMs={fullMedian}; ratio={localMedian / fullMedian}");
        Assert.NotEmpty(candidate.Transfers);
        Assert.True(localMedian <= fullMedian * 0.5, $"{name}: local {localMedian:F2} ms / full {fullMedian:F2} ms exceeds 50%.");
    }
}
