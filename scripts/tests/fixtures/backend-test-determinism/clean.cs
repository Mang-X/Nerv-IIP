using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

public static class CleanFixture
{
    public static Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static object[] ReadOnlyState()
    {
        var currentCulture = CultureInfo.CurrentCulture;
        var renewalStore = new FailingRenewalStore();
        var unrelatedInterval = TimeSpan.FromMilliseconds(100);
        return [currentCulture, renewalStore, unrelatedInterval];
    }
}
