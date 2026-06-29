using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.InspectionRecords;

public interface IInspectionUomConversionClient
{
    Task<IReadOnlyCollection<InspectionUomConversion>> GetConversionsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken);
}

public sealed class NullInspectionUomConversionClient : IInspectionUomConversionClient
{
    public static readonly NullInspectionUomConversionClient Instance = new();

    private NullInspectionUomConversionClient()
    {
    }

    public Task<IReadOnlyCollection<InspectionUomConversion>> GetConversionsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<InspectionUomConversion>>([]);
    }
}

public interface IInspectionSourceDocumentVerifier
{
    Task<InspectionSourceDocumentVerification> VerifyAsync(
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        decimal inspectedQuantity,
        CancellationToken cancellationToken);
}

public sealed record InspectionSourceDocumentVerification(
    bool Exists,
    string? SkuCode = null,
    decimal? Quantity = null,
    string? Message = null);

public sealed class NullInspectionSourceDocumentVerifier : IInspectionSourceDocumentVerifier
{
    public static readonly NullInspectionSourceDocumentVerifier Instance = new();

    private NullInspectionSourceDocumentVerifier()
    {
    }

    public Task<InspectionSourceDocumentVerification> VerifyAsync(
        string organizationId,
        string environmentId,
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string skuCode,
        decimal inspectedQuantity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new InspectionSourceDocumentVerification(true, skuCode, inspectedQuantity));
    }
}
