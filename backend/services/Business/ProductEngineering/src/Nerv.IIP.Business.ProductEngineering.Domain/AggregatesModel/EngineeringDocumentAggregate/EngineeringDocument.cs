using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;
using static Nerv.IIP.Business.ProductEngineering.Domain.ProductEngineeringGuards;

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
        string? itemCode,
        string fileId,
        string fileName,
        string contentType,
        string documentType,
        EngineeringVersionStatus status,
        string? operationCode,
        string? workCenterCode,
        string? routingCode,
        string? routingRevision,
        DateOnly? effectiveDate)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        DocumentNumber = Required(documentNumber);
        Revision = Required(revision);
        ItemCode = string.IsNullOrWhiteSpace(itemCode) ? null : itemCode.Trim();
        FileId = Required(fileId);
        FileName = Required(fileName);
        ContentType = Required(contentType);
        DocumentType = Required(documentType);
        Status = status;
        OperationCode = Optional(operationCode);
        WorkCenterCode = Optional(workCenterCode);
        RoutingCode = Optional(routingCode);
        RoutingRevision = Optional(routingRevision);
        EffectiveDate = effectiveDate;
        RegisteredAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DocumentNumber { get; private set; } = string.Empty;
    public string Revision { get; private set; } = string.Empty;
    public string? ItemCode { get; private set; }
    public string FileId { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public string DocumentType { get; private set; } = string.Empty;
    public EngineeringVersionStatus Status { get; private set; } = EngineeringVersionStatus.Published;
    public string? OperationCode { get; private set; }
    public string? WorkCenterCode { get; private set; }
    public string? RoutingCode { get; private set; }
    public string? RoutingRevision { get; private set; }
    public DateOnly? EffectiveDate { get; private set; }
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
        return Register(
            organizationId,
            environmentId,
            documentNumber,
            revision,
            null,
            fileId,
            fileName,
            contentType,
            documentType);
    }

    public static EngineeringDocument Register(
        string organizationId,
        string environmentId,
        string documentNumber,
        string revision,
        string? itemCode,
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
            itemCode,
            fileId,
            fileName,
            contentType,
            documentType,
            EngineeringVersionStatus.Published,
            null,
            null,
            null,
            null,
            null);
        document.AddDomainEvent(new EngineeringDocumentRegisteredDomainEvent(document));
        return document;
    }

    public static EngineeringDocument PublishSop(
        string organizationId,
        string environmentId,
        string documentNumber,
        string revision,
        string operationCode,
        string? workCenterCode,
        string? routingCode,
        string? routingRevision,
        DateOnly effectiveDate,
        string fileId,
        string fileName,
        string contentType)
    {
        var document = new EngineeringDocument(
            organizationId,
            environmentId,
            documentNumber,
            revision,
            null,
            fileId,
            fileName,
            contentType,
            "sop",
            EngineeringVersionStatus.Published,
            operationCode,
            workCenterCode,
            routingCode,
            routingRevision,
            effectiveDate);
        document.AddDomainEvent(new EngineeringDocumentRegisteredDomainEvent(document));
        return document;
    }

    public void Archive(string reason)
    {
        _ = Required(reason);
        if (Status != EngineeringVersionStatus.Published)
        {
            throw new InvalidOperationException("Only published engineering document versions can be archived.");
        }

        Status = EngineeringVersionStatus.Archived;
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
