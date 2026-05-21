namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record ValidateMasterDataReferencesResponse(bool Valid, IReadOnlyCollection<MasterDataReferenceResponse> References);

public sealed record ValidateMasterDataReferencesQuery(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<MasterDataReferenceRequest> References) : IQuery<ValidateMasterDataReferencesResponse>;

public sealed class ValidateMasterDataReferencesQueryHandler(ISender sender)
    : IQueryHandler<ValidateMasterDataReferencesQuery, ValidateMasterDataReferencesResponse>
{
    public async Task<ValidateMasterDataReferencesResponse> Handle(ValidateMasterDataReferencesQuery request, CancellationToken cancellationToken)
    {
        var resolved = await sender.Send(
            new ResolveMasterDataReferencesQuery(request.OrganizationId, request.EnvironmentId, request.References),
            cancellationToken);
        var valid = resolved.References.All(reference => reference.Exists && reference.Active);
        return new ValidateMasterDataReferencesResponse(valid, resolved.References);
    }
}
