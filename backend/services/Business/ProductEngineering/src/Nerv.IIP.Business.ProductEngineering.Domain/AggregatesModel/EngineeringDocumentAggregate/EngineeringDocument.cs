using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;

namespace Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;

public partial record EngineeringDocumentId : IGuidStronglyTypedId;

public sealed class EngineeringDocument : Entity<EngineeringDocumentId>, IAggregateRoot
{
    private EngineeringDocument()
    {
    }

    private EngineeringDocument(
        string organizationId,
        string environmentId,
        string documentNumber,
        string revision,
        string fileId,
        string fileName,
        string contentType,
        string documentType)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        DocumentNumber = Required(documentNumber);
        Revision = Required(revision);
        FileId = Required(fileId);
        FileName = Required(fileName);
        ContentType = Required(contentType);
        DocumentType = Required(documentType);
        RegisteredAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DocumentNumber { get; private set; } = string.Empty;
    public string Revision { get; private set; } = string.Empty;
    public string FileId { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public string DocumentType { get; private set; } = string.Empty;
    public DateTime RegisteredAtUtc { get; private set; }

    public static EngineeringDocument Register(
        string organizationId,
        string environmentId,
        string documentNumber,
        string revision,
        string fileId,
        string fileName,
        string contentType,
        string documentType)
    {
        var document = new EngineeringDocument(
            organizationId,
            environmentId,
            documentNumber,
            revision,
            fileId,
            fileName,
            contentType,
            documentType);
        document.AddDomainEvent(new EngineeringDocumentRegisteredDomainEvent(document));
        return document;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }
}
