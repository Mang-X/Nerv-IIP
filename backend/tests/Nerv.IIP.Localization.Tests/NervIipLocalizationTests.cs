using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nerv.IIP.Localization;

namespace Nerv.IIP.Localization.Tests;

public sealed class NervIipLocalizationTests
{
    [Fact]
    public async Task RequestLocalization_WithEnUsAcceptLanguage_SetsCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
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
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
