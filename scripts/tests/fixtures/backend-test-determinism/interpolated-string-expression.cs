using System.Threading.Tasks;

public static class InterpolatedStringExpressionFixture
{
    public static string Run()
    {
        return $"{Task.Delay(655)}";
    }
}
