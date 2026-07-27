using System.Globalization;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>
/// L1 背景历史引擎的开关与参数（ProductEngineering 侧）。
///
/// - <c>LeaderDemo:History:Enabled</c>：总开关（AppHost 在 leader-demo profile 下默认开）。
/// - <c>LeaderDemo:History:Scale</c>：缩放比例，<c>1.0</c> = 全量，<c>0.1</c> = 十分之一的快速验证。
/// - <c>LeaderDemo:History:AsOfDate</c>：历史截止日（<c>yyyy-MM-dd</c>），缺省取当前 UTC 日期。
///   AppHost 会把同一个值发给所有参与 L1 的服务——各服务若跨零点分别启动而各自取「今天」，
///   周次切片会错开一周，跨域号段的配对随之断裂。
///
/// 与 ERP / MES / Quality / Approval 侧按同一字面量重复声明（各侧不共享程序集，靠黄金向量测试防漂移）。
/// </summary>
public static class WorldHistoryConfiguration
{
    public const string EnabledKey = "LeaderDemo:History:Enabled";
    public const string ScaleKey = "LeaderDemo:History:Scale";
    public const string AsOfDateKey = "LeaderDemo:History:AsOfDate";

    public const double DefaultScale = 1.0d;
    public const double MinimumScale = 0.001d;
    public const double MaximumScale = 10.0d;

    public static bool IsEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetValue(EnabledKey, false);
    }

    /// <summary>读取缩放比例；非法值直接失败而不是悄悄回落，避免「以为跑了全量其实跑了 0.1」。</summary>
    public static double ResolveScale(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var scale = configuration.GetValue(ScaleKey, DefaultScale);
        if (scale is < MinimumScale or > MaximumScale || double.IsNaN(scale))
        {
            throw new InvalidOperationException(
                $"'{ScaleKey}' must be within [{MinimumScale}, {MaximumScale}] but was {scale.ToString(CultureInfo.InvariantCulture)}.");
        }

        return scale;
    }

    /// <summary>读取历史截止日；缺省为当前 UTC 日期，早于上线日则夹到上线日。</summary>
    public static DateOnly ResolveAsOfDate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[AsOfDateKey];
        DateOnly asOfDate;
        if (string.IsNullOrWhiteSpace(configured))
        {
            asOfDate = DateOnly.FromDateTime(DateTime.UtcNow);
        }
        else if (!DateOnly.TryParseExact(configured, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out asOfDate))
        {
            throw new InvalidOperationException($"'{AsOfDateKey}' must be an ISO date (yyyy-MM-dd) but was '{configured}'.");
        }

        return asOfDate < WorldHistoryCalendar.GoLiveDate ? WorldHistoryCalendar.GoLiveDate : asOfDate;
    }
}
