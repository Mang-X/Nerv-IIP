using Nerv.IIP.Business.Mes.Web.Application.Quality;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 与首件确认无关的报工用例用它放行首件门禁（#2780）——报工路径每次都会问 Quality 首件进度。
/// 门禁本身的判据由 <see cref="MesFirstArticleReportGateTests"/> 与 <see cref="HttpMesFirstArticleGateTests"/> 承担。
/// </summary>
internal sealed class TestMesFirstArticleGate : IMesFirstArticleGate
{
    public static TestMesFirstArticleGate Allowing { get; } = new();

    public Task EnsureBatchReportAllowedAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
