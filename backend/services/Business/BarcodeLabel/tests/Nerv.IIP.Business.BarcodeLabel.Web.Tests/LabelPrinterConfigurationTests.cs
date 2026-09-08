using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class LabelPrinterConfigurationTests
{
    [Fact]
    public void Production_requires_an_explicit_printer_mode()
    {
        var validator = new LabelPrinterOptionsValidator(new TestEnvironment("Production"));

        var result = validator.Validate(null, new LabelPrinterOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("LabelPrinter:Mode", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void Simulated_mode_is_only_valid_in_explicit_non_delivery_environments(
        string environmentName,
        bool expectedSuccess)
    {
        var validator = new LabelPrinterOptionsValidator(new TestEnvironment(environmentName));

        var result = validator.Validate(null, new LabelPrinterOptions { Mode = "simulated" });

        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("unknown")]
    public void Unknown_or_disabled_mode_is_rejected(string mode)
    {
        var validator = new LabelPrinterOptionsValidator(new TestEnvironment("Development"));

        var result = validator.Validate(null, new LabelPrinterOptions { Mode = mode });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("LabelPrinter:Mode", StringComparison.Ordinal));
    }

    [Fact]
    public void Zpl_tcp_mode_accepts_a_complete_enabled_route()
    {
        var result = Validate(ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("connect-timeout")]
    [InlineData("write-timeout")]
    [InlineData("dpi")]
    [InlineData("language")]
    [InlineData("capabilities")]
    [InlineData("enabled")]
    public void Zpl_tcp_mode_rejects_an_incomplete_or_invalid_route(string invalidField)
    {
        var options = ValidOptions();
        var route = options.Printers[0];
        options.Printers[0] = invalidField switch
        {
            "id" => route with { Id = " " },
            "host" => route with { Host = "tcp://printer.example.test:9100" },
            "port" => route with { Port = 0 },
            "connect-timeout" => route with { ConnectTimeoutSeconds = 0 },
            "write-timeout" => route with { WriteTimeoutSeconds = 0 },
            "dpi" => route with { Dpi = 200 },
            "language" => route with { Language = "raw" },
            "capabilities" => route with { Capabilities = "" },
            "enabled" => route with { Enabled = false },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField)),
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("LabelPrinter:Printers", StringComparison.Ordinal));
    }

    [Fact]
    public void Zpl_tcp_mode_rejects_duplicate_printer_ids()
    {
        var options = ValidOptions();
        options.Printers.Add(options.Printers[0] with { Host = "printer-02.example.test" });

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Web_host_validates_label_printer_options_on_start()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                builder.UseSetting("LabelPrinter:Mode", "unknown");
            });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("LabelPrinter:Mode", exception.ToString(), StringComparison.Ordinal);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(LabelPrinterOptions options) =>
        new LabelPrinterOptionsValidator(new TestEnvironment("Production")).Validate(null, options);

    private static LabelPrinterOptions ValidOptions() => new()
    {
        Mode = "zpl-tcp",
        Printers =
        [
            new LabelPrinterRouteOptions
            {
                Id = "printer-01",
                Host = "printer-01.example.test",
                Port = 9100,
                ConnectTimeoutSeconds = 5,
                WriteTimeoutSeconds = 10,
                Dpi = 203,
                Language = "zpl",
                Capabilities = "code128,gs1-128,qr,datamatrix,gs1-datamatrix",
                Enabled = true,
            }
        ],
    };

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BarcodeLabel.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
