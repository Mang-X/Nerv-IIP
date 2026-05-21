namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayHttpClientResilienceTests
{
    [Fact]
    public void Non_idempotent_gateway_clients_do_not_use_standard_retry_handler()
    {
        var program = File.ReadAllText(FindProgramFile());
        var helperStart = program.IndexOf("AddGatewayNonIdempotentSafeResilience(this", StringComparison.Ordinal);
        Assert.NotEqual(-1, helperStart);

        var helperBody = program[helperStart..];

        Assert.DoesNotContain("AddStandardResilienceHandler", helperBody);
        Assert.DoesNotContain("Retry", helperBody);
    }

    private static string FindProgramFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var programPath = Path.Combine(
                directory.FullName,
                "src",
                "Nerv.IIP.PlatformGateway.Web",
                "Program.cs");
            if (File.Exists(programPath))
            {
                return programPath;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate gateway Program.cs.");
    }
}
