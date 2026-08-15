using System.Threading.Tasks;

public static class InterpolatedVerbatimExpressionFixture
{
    public static string Run()
    {
        return $@"{Task.Delay(656)}";
    }
}
