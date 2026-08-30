namespace Nerv.IIP.Iam.Web.Application.Seed;

/// <summary>
/// IAM World Bible worker seed 的唯一入口判定。
/// </summary>
public static class WorldBibleSeedGate
{
    /// <summary>
    /// 世界观全量开启时保留既有 worker seed 行为；世界观关闭时，仅在当前进程
    /// 显式提供非空 <c>Iam:Seed:DemoWorkerPassword</c> 时开启 PDA worker seed。
    /// 本判定只返回布尔值，不记录或持久化口令。
    /// </summary>
    public static bool ShouldRunWorkerSeeds(bool worldEnabled, string? demoWorkerPassword) =>
        worldEnabled || !string.IsNullOrWhiteSpace(demoWorkerPassword);
}
