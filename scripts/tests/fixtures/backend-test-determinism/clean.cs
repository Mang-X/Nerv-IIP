using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

public static class CleanFixture
{
    private const string CodeExample = "Task.Delay(1); Thread.Sleep(1); CultureInfo.CurrentCulture = null!;";
    private const string VerbatimCodeExample = @"Task.Delay(1); // Thread.Sleep(1);";
    private const string RawCodeExample = """
        Environment.SetEnvironmentVariable("RAW_STRING_ONLY", "1");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        """;

    public static Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static object[] ReadOnlyState()
    {
        var currentCulture = CultureInfo.CurrentCulture;
        var renewalStore = new FailingRenewalStore();
        var unrelatedInterval = TimeSpan.FromMilliseconds(100);
        var comparison = CultureInfo.CurrentCulture == CultureInfo.InvariantCulture;
        var validatorComparison = ValidatorOptions.Global.LanguageManager == null;
        var quote = '\'';

        // Task.Delay(1); Thread.Sleep(1); Host=127.0.0.1;Port=1;
        /*
           Task.Delay(1);
           Environment.SetEnvironmentVariable("COMMENT_ONLY", "1");
           CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
           Host=127.0.0.1;Port=1;
        */

        return [currentCulture, renewalStore, unrelatedInterval, comparison, validatorComparison, quote, CodeExample, VerbatimCodeExample, RawCodeExample];
    }
}
