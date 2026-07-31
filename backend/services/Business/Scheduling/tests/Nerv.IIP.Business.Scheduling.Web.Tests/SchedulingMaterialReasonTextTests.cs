using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

/// <summary>
/// MAN-698 台账 #35 的**跨服务形态锚**。
///
/// 缺料原因串的形态 <c>CODE: 中文事实</c> 是跨服务约定，但三处实现各自独立
/// （MES <c>MaterialReadinessGuards</c>、本服务 <c>SchedulingMaterialReasonText</c>、
/// 前端 <c>describeMesReadinessReason</c>）——服务边界不共享库，前端更不可能引用后端代码。
/// 既然共享不了实现，就**用断言把形态钉住**：这里的期望串与 MES 侧
/// <c>Material_shortage_reason_is_chinese_and_strips_the_code_for_user_facing_messages</c>
/// 里的期望串是逐字一致的；任何一侧改了措辞，另一侧的用例会红，避免两端悄悄漂移。
/// </summary>
public sealed class SchedulingMaterialReasonTextTests
{
    [Fact]
    public void Shortage_reason_matches_the_cross_service_shape()
    {
        var withLot = SchedulingMaterialReasonText.FormatShortage("MAT-OIL", "LOT-OIL-A", 2.5m);
        var withoutLot = SchedulingMaterialReasonText.FormatShortage("MAT-BEARING", null, 7m);

        // 与 MES 侧逐字一致（Nerv.IIP.Business.Mes.Web.Tests.MesTaskScopeQueryTests 同名期望）。
        Assert.Equal("MATERIAL_SHORTAGE: 物料 MAT-OIL，批次 LOT-OIL-A 缺口 2.5", withLot);
        Assert.Equal("MATERIAL_SHORTAGE: 物料 MAT-BEARING 缺口 7", withoutLot);
        Assert.DoesNotContain("shortage ", withLot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void User_facing_text_strips_codes_and_drops_pure_code_noise()
    {
        var described = SchedulingMaterialReasonText.DescribeForUser(
        [
            SchedulingMaterialReasonText.FormatShortage("MAT-OIL", "LOT-OIL-A", 2.5m),
            "MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: 工单缺少齐套需求快照。",
        ]);

        Assert.Equal(2, described.Count);
        Assert.DoesNotContain(described, x => x.Contains("MATERIAL_SHORTAGE", StringComparison.Ordinal));
        Assert.Contains("物料 MAT-OIL，批次 LOT-OIL-A 缺口 2.5", described);
        Assert.Contains("工单缺少齐套需求快照。", described);
    }

    [Fact]
    public void Bare_legacy_codes_are_translated_or_dropped_never_shown_raw()
    {
        // 裸码没有 `CODE: ` 前缀也没有中文,剥不掉:已知的翻中文,未知的丢弃。
        Assert.Equal(["物料缺料"], SchedulingMaterialReasonText.DescribeForUser(["material-shortage"]));
        Assert.Equal(["物料缺料"], SchedulingMaterialReasonText.DescribeForUser(["material.shortage"]));
        Assert.Empty(SchedulingMaterialReasonText.DescribeForUser(["some-internal-code", "  "]));
    }

    [Fact]
    public void Chinese_colon_inside_the_explanation_is_not_treated_as_a_code_separator()
    {
        Assert.Equal("物料齐套未满足：还差 3 件", SchedulingMaterialReasonText.StripCode("物料齐套未满足：还差 3 件"));
    }
}
