using System.Threading;
using System.Threading.Tasks;

public static class UnexplainedDelayFixture
{
    public static async Task RunAsync()
    {
        await Task.Delay(250);
        Thread.Sleep(25);
    }
}
