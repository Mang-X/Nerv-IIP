namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public sealed record LabelTemplateAssetReference(
    string FileId,
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode);

public sealed record VerifiedLabelTemplateAsset(
    string FileId,
    string Sha256,
    string Json);

public interface ILabelTemplateAssetPort
{
    Task<VerifiedLabelTemplateAsset> GetVerifiedAsync(
        LabelTemplateAssetReference reference,
        CancellationToken cancellationToken);
}
