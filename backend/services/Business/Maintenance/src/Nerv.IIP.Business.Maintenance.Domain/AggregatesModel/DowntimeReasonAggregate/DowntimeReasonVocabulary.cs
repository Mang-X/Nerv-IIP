namespace Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;

/// <summary>
/// 停机原因分类与 OEE 损失类别的受控码（#3855）。词表归 Maintenance 所有：新增 / 修改停机原因的命令按这里校验，
/// 产品基线种子引用这里的常量，控制台按这些码显示中文。
/// 分类依据 TPM 六大损失与国内 OEE 实践；损失类别对应 OEE 三率，计划停机不计入损失。
/// <para>
/// 聚合本身不校验（历史数据与演示种子可能带着更早的自由文本），校验落在写命令入口。
/// </para>
/// </summary>
public static class DowntimeReasonVocabulary
{
    public static class ReasonCategories
    {
        public const string Breakdown = "breakdown";
        public const string Setup = "setup";
        public const string MinorStop = "minor-stop";
        public const string Process = "process";
        public const string Quality = "quality";
        public const string Material = "material";
        public const string Labor = "labor";
        public const string External = "external";
        public const string Planned = "planned";
        public const string Unclassified = "unclassified";

        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        {
            Breakdown, Setup, MinorStop, Process, Quality, Material, Labor, External, Planned, Unclassified,
        };
    }

    public static class LossCategories
    {
        public const string Availability = "availability";
        public const string Performance = "performance";
        public const string Quality = "quality";
        public const string Planned = "planned";
        public const string Unclassified = "unclassified";

        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        {
            Availability, Performance, Quality, Planned, Unclassified,
        };
    }
}
