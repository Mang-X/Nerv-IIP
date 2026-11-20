using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>显式启用 Development 现场作业池种子。</summary>
public static class WmsWorkPoolMembershipSeedGate
{
    public const string EnabledKey = "Wms:WorkPoolSeed:Enabled";

    public static bool ShouldSeed(IConfiguration configuration, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.GetValue<bool>(EnabledKey))
        {
            return false;
        }

        if (!isDevelopment)
        {
            throw new InvalidOperationException(
                $"{EnabledKey}=true is only allowed for BusinessWms in Development.");
        }

        return true;
    }
}
