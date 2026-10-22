using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// WMS 现场演示作业池种子的显式开关。
///
/// 这个开关只负责补齐最小的现场资格边界；完整世界历史仍由
/// <see cref="WorldHistoryConfiguration"/> 独立控制，二者不能同时写同一套 WMS 事实。
/// </summary>
public static class WmsWorkPoolMembershipSeedGate
{
    public const string EnabledKey = "LeaderDemo:Wms:WorkPoolSeed:Enabled";

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

        // History owns the complete WMS work-pool and document graph. The minimum
        // seed must stay off in that mode so it cannot create a second fact set.
        return !WorldHistoryConfiguration.IsEnabled(configuration);
    }
}
