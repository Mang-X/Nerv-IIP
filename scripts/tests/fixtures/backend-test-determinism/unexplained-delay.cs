using System.Threading;
using System.Threading.Tasks;

public static class UnexplainedDelayFixture
{
    public static async Task RunAsync()
    {
        var empty = "";
        var formatted = $"{(true ? "yes" : "no")}";
        await Task.Delay(250);
        await Task
            .Delay(125);
        Thread.Sleep(25);
    }
}
