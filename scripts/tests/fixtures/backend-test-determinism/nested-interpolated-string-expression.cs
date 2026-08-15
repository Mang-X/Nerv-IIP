using System.Threading.Tasks;

public static class NestedInterpolatedStringExpressionFixture
{
    public static string Run()
    {
        return $"outer {$"inner {Task.Delay(657)}"}";
    }
}
