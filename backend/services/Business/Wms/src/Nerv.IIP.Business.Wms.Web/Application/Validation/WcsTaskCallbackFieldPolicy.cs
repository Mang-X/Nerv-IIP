namespace Nerv.IIP.Business.Wms.Web.Application.Validation;

/// <summary>
/// WCS 回调两条命令（<c>CompleteWcsTaskCommand</c> / <c>FailWcsTaskCommand</c>）入参上界的唯一出处（#3305）。
/// </summary>
/// <remarks>
/// <para><b>为什么这些数字住在这里而不是直接写进 <c>MaximumLength(...)</c></b>：
/// 它们不是拍出来的，而是 <c>wcs_tasks</c> 表对应列的宽度。
/// <c>WcsTaskCallbackValidatorTests.Declared_bounds_equal_the_carrying_column_widths</c>
/// 从 EF 模型读出这几列的 <c>GetMaxLength()</c> 与本类逐条对撞，
/// 任一侧单边改动即红——所以「不手抄」在本仓的意思是**数字只有一份且被模型钉住**。</para>
///
/// <para><b>射程边界一：本类不覆盖 <c>failure_message</c>。</b>
/// 那一列在 #3305 里被**去掉**了上界（改 <c>text</c>）：它是外部 WCS 回传的原始诊断报文，
/// 人为上界换来的是「任务卡在执行中、要现场人工介入」。因此这里只有 <see cref="FailureCodeMaxLength"/>，
/// 没有 <c>FailureMessageMaxLength</c>——**别因为对称好看就补一个回来**。
/// 摘要装不装得下由 Notification 侧自己渲染时保证（<c>NotificationSummaryText</c>）。</para>
///
/// <para><b>射程边界二：这些上界与「今天能不能真的拦到东西」不是同一件事。</b>
/// 逐字段的实际可达性由 <c>WcsTaskCallbackValidatorTests</c> 在**真实 host 管道**里逐格量出来并写在那里，
/// 不在本类断言。别把「这里声明了上界」读成「这条上界在管道里拦得住」。</para>
/// </remarks>
public static class WcsTaskCallbackFieldPolicy
{
    /// <summary><c>wcs_tasks.organization_id</c> / <c>wcs_tasks.environment_id</c> 列宽。</summary>
    public const int TenantIdMaxLength = 100;

    /// <summary><c>wcs_tasks.external_task_id</c> 列宽。</summary>
    public const int ExternalTaskIdMaxLength = 150;

    /// <summary><c>wcs_tasks.failure_code</c> 列宽。</summary>
    public const int FailureCodeMaxLength = 100;
}
