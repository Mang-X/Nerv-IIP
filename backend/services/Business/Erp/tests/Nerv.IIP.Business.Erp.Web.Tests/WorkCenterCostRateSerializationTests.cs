using System.Text.Json;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using NetCorePal.Extensions.Dto;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class WorkCenterCostRateSerializationTests
{
    [Fact]
    public void Configure_response_uses_the_public_strong_id_wire_shape()
    {
        var id = Guid.Parse("018f4b87-9a0c-7a6b-9a3a-5fd5825c2df9");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddNetCorePalJsonConverters();

        var json = JsonSerializer.Serialize(
            new ConfigureWorkCenterCostRateResponse(new WorkCenterCostRateId(id)).AsResponseData(),
            options);
        using var document = JsonDocument.Parse(json);
        var idElement = document.RootElement
            .GetProperty("data")
            .GetProperty("workCenterCostRateId");

        Assert.Equal(JsonValueKind.String, idElement.ValueKind);
        Assert.Equal(id, idElement.GetGuid());
    }
}
