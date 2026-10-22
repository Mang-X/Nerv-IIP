using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Endpoints.BarcodeLabel;

public sealed record ListLabelPrintersRequest(string OrganizationId, string EnvironmentId);

public sealed record LabelPrinterSummary(string PrinterId, string Name);

public sealed record ListLabelPrintersResponse(IReadOnlyCollection<LabelPrinterSummary> Printers);

public sealed class ListLabelPrintersEndpoint(IOptions<LabelPrinterOptions> options)
    : BarcodeLabelEndpoint<ListLabelPrintersRequest, ResponseData<ListLabelPrintersResponse>>
{
    public override void Configure() =>
        ConfigureBarcodeLabelContract(BarcodeLabelEndpointContracts.Get<ListLabelPrintersEndpoint>());

    public override async Task HandleAsync(ListLabelPrintersRequest req, CancellationToken ct)
    {
        var printers = options.Value.Printers
            .Where(printer => printer.Enabled)
            .OrderBy(printer => printer.Id, StringComparer.Ordinal)
            .Select(printer => new LabelPrinterSummary(
                printer.Id, string.IsNullOrWhiteSpace(printer.Name) ? printer.Id : printer.Name))
            .ToArray();
        await Send.OkAsync(new ListLabelPrintersResponse(printers).AsResponseData(), cancellation: ct);
    }
}
