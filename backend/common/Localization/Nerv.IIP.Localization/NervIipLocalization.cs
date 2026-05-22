using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Localization;

public static class NervIipLocalization
{
    public const string DefaultCultureName = "zh-CN";
    public static readonly IReadOnlyList<string> SupportedCultureNames = ["zh-CN", "en-US"];

    public static IServiceCollection AddNervIipLocalization(this IServiceCollection services)
    {
        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = SupportedCultureNames
                .Select(CultureInfo.GetCultureInfo)
                .ToArray();

            options.DefaultRequestCulture = new RequestCulture(DefaultCultureName);
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
            options.RequestCultureProviders =
            [
                new AcceptLanguageHeaderRequestCultureProvider()
            ];
        });

        return services;
    }

    public static IApplicationBuilder UseNervIipRequestLocalization(this IApplicationBuilder app)
    {
        return app.UseRequestLocalization();
    }
}
