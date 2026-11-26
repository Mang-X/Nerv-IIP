using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.MasterData;

public sealed class MesMaterialIssueUnitReader(
    MesMasterDataHttpClient masterData, IInternalServiceTokenProvider tokenProvider)
{
    public async Task<string> ReadBaseUnitAsync(
        string organizationId, string environmentId, string materialId, CancellationToken cancellationToken)
    {
        var scope = $"organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}";
        var material = await ReadAsync("sku", materialId, scope, cancellationToken);
        if (material is not { Active: true })
            throw new KnownException("物料不存在或已停用，请核对物料编号。");

        var unitCode = material.BaseUomCode?.Trim();
        if (string.IsNullOrEmpty(unitCode))
            throw InvalidUnit();

        var unit = await ReadAsync("unit-of-measure", unitCode, scope, cancellationToken);
        if (unit is not { Active: true })
            throw InvalidUnit();

        return unitCode;
    }

    private static KnownException InvalidUnit() =>
        new("物料缺少有效的基本计量单位，请联系主数据维护人员。");

    private async Task<Resource?> ReadAsync(string type, string code, string scope, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/business/v1/master-data/resources/{type}/{Uri.EscapeDataString(code)}?{scope}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);
        using var response = await masterData.HttpClient.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new KnownException("暂时无法核对物料及计量单位，请稍后重试。");

        var envelope = await response.Content.ReadFromJsonAsync<ResponseData<Resource>>(ct);
        return envelope is { Success: true } ? envelope.Data : null;
    }

    private sealed record Resource(bool Active, string? BaseUomCode);
}
