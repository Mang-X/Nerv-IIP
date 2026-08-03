using System.Threading.Tasks;

public static class RawEmptyFollowedByDelayFixture
{
    public static async Task RunAsync()
    {
        var empty = """""";
        await Task
            .Delay(321);
    }
}
