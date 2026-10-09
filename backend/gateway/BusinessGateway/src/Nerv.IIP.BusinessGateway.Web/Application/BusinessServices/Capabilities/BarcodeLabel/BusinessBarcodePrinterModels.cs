using System.ComponentModel.DataAnnotations;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleBarcodePrinterListRequest(string OrganizationId, string EnvironmentId);

public sealed record BusinessConsoleBarcodePrinterItem(
    [property: Required] string PrinterId,
    [property: Required] string Name);

public sealed record BusinessConsoleBarcodePrinterListResponse(
    [property: Required] IReadOnlyCollection<BusinessConsoleBarcodePrinterItem> Printers);
