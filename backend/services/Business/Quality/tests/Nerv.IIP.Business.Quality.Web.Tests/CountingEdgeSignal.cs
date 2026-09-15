using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 把「<paramref name="subject"/> 已经发生第 N 次」变成可等待的边沿，由发生的那一刻发布。
/// </summary>
/// <param name="subject">
/// 被等待事实的完整措辞，直接拼进诊断（如 <c>"the scanner to finish a release fact backlog scope
/// scan"</c>）。动词属于调用方：同一个计数原语既用于「已派发第 N 条命令」，也用于「已完成第 N 次
/// 扫描」，把动词写死在原语里只会让其中一方的诊断说谎。
/// </param>
/// <remarks>
/// <para>
/// 这些边沿计的是被测 <c>BackgroundService</c> 在**第一次、立即执行**的那一趟里做的事——它跑在
/// <c>PeriodicTimer</c> 之前，因此根本不是时间事实，而是「后台任务的续体已经跑到这里」，恰是注入的
/// <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/> **无法建模**的那一件事。
/// </para>
/// <para>
/// 用 <see cref="Eventually.WaitAsync"/> 墙钟轮询它，会让判决取决于「轮询循环自己的续体在真实时间
/// 预算内被服务了几次」：第一次观测必然不满足，通过就要求预算耗尽前至少再服务一轮。跑满的 runner
/// 上饿死的正是这些续体——#3323 在 CI 上记下的就是这个形态（2s / 10ms 窗口里只拿到 2 次观测）。
/// 把假时钟交给这个窗口既没用也不可用：等待在飞时谁都不许推进这口时钟，而它同时驱动着被测对象的
/// <c>PeriodicTimer</c>；把推进委托给别人又会让轮询节奏耦合上被测对象自己的 tick 语义。
/// </para>
/// <para>
/// 边沿只需要一次续体，健康的一跑根本不看时钟。<see cref="BoundedSignal"/> 仍用真实时钟兜底，只为
/// 预算在这里还值得做的那一件事：把**丢失的**边沿变成诊断，而不是挂住整跑。
/// </para>
/// </remarks>
internal sealed class CountingEdgeSignal(string subject)
{
    private readonly Lock gate = new();
    private readonly List<(int ExpectedCount, TaskCompletionSource Reached)> waiters = [];
    private int observed;

    public int Observed => Volatile.Read(ref observed);

    public void Record()
    {
        var reached = Interlocked.Increment(ref observed);
        List<TaskCompletionSource>? released = null;
        lock (gate)
        {
            for (var index = waiters.Count - 1; index >= 0; index--)
            {
                if (waiters[index].ExpectedCount > reached)
                {
                    continue;
                }

                (released ??= []).Add(waiters[index].Reached);
                waiters.RemoveAt(index);
            }
        }

        foreach (var waiter in released ?? [])
        {
            waiter.TrySetResult();
        }
    }

    /// <remarks>
    /// 「已经到数」的检查与登记共用 <see cref="gate"/>（释放扫描也走它），所以卡在两者之间的那一次
    /// <see cref="Record"/> 不会被漏掉：它要么看见 waiter 已登记，要么早已把检查读的那个计数加上去了。
    /// </remarks>
    public Task WaitForAsync(int expectedCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedCount, 1);

        TaskCompletionSource reached;
        lock (gate)
        {
            if (Volatile.Read(ref observed) >= expectedCount)
            {
                return Task.CompletedTask;
            }

            reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters.Add((expectedCount, reached));
        }

        return BoundedSignal.ObserveAsync(
            reached.Task,
            $"{subject} #{expectedCount}",
            () => $"observed={Observed}; expected>={expectedCount}");
    }
}
