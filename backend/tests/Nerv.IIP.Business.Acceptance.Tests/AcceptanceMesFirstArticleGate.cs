using Nerv.IIP.Business.Mes.Web.Application.Quality;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// 验收用例的被测对象是 MES↔Inventory 的线边物料动线，不是首件确认（#2780）：门禁放行，
/// 判据本身由 MES 侧的 <c>MesFirstArticleReportGateTests</c> / <c>HttpMesFirstArticleGateTests</c> 承担。
/// </summary>
internal sealed class AcceptanceMesFirstArticleGate : IMesFirstArticleGate
{
    public static AcceptanceMesFirstArticleGate Allowing { get; } = new();

    public Task EnsureBatchReportAllowedAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
