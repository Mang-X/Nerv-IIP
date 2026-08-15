using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nerv.IIP.Localization;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Localization.Tests;

public sealed class NervIipLocalizationTests
{
    [Fact]
    public async Task RequestLocalization_WithEnUsAcceptLanguage_SetsCurrentCulture()
    {
        // The scope serialises every culture mutator in the assembly and restores the exact prior
        // values on dispose, so neither the zh-CN precondition nor the en-US the middleware installs
        // can leak onwards.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState.UseCulture("zh-CN");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNervIipLocalization();
        using var provider = services.BuildServiceProvider();

        string? cultureName = null;
        string? uiCultureName = null;
        var middleware = new RequestLocalizationMiddleware(
            _ =>
            {
                cultureName = CultureInfo.CurrentCulture.Name;
                uiCultureName = CultureInfo.CurrentUICulture.Name;
                return Task.CompletedTask;
            },
            provider.GetRequiredService<IOptions<RequestLocalizationOptions>>(),
            provider.GetRequiredService<ILoggerFactory>());
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "en-US,en;q=0.9";

        await middleware.Invoke(context);

        Assert.Equal("en-US", cultureName);
        Assert.Equal("en-US", uiCultureName);
    }
}
