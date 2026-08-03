using System.Threading.Tasks;

public static class InterpolatedRawExpressionFixture
{
    public static string Run()
    {
        return $"""{Task.Delay(654)}""";
    }
}
