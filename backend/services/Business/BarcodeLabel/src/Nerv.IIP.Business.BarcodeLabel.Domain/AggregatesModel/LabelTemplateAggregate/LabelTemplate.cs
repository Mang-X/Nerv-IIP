using Nerv.IIP.Business.BarcodeLabel.Domain;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

public partial record LabelTemplateId : IGuidStronglyTypedId;

public sealed class LabelTemplate : Entity<LabelTemplateId>, IAggregateRoot
{
    private static readonly HashSet<string> SupportedStatuses = ["active", "inactive"];

    private LabelTemplate()
    {
    }

    private LabelTemplate(
        string organizationId,
        string environmentId,
        string templateCode,
        string templateName,
        string templateFileId,
        string variableSchemaJson,
        string status)
    {
        Id = new LabelTemplateId(Guid.CreateVersion7());
        OrganizationId = BarcodeLabelText.Required(organizationId, nameof(organizationId));
        EnvironmentId = BarcodeLabelText.Required(environmentId, nameof(environmentId));
        TemplateCode = BarcodeLabelText.Required(templateCode, nameof(templateCode));
        TemplateName = BarcodeLabelText.Required(templateName, nameof(templateName));
        TemplateFileId = CleanFileId(templateFileId);
        VariableSchemaJson = BarcodeLabelText.Required(variableSchemaJson, nameof(variableSchemaJson));
        Status = BarcodeLabelText.Supported(status, SupportedStatuses, nameof(status));
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string TemplateCode { get; private set; } = string.Empty;
    public string TemplateName { get; private set; } = string.Empty;
    public string TemplateFileId { get; private set; } = string.Empty;
    public string VariableSchemaJson { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static LabelTemplate Create(
        string organizationId,
        string environmentId,
        string templateCode,
        string templateName,
        string templateFileId,
        string variableSchemaJson,
        string status)
    {
        return new LabelTemplate(organizationId, environmentId, templateCode, templateName, templateFileId, variableSchemaJson, status);
    }

    public void Update(string templateName, string templateFileId, string variableSchemaJson, string status)
    {
        TemplateName = BarcodeLabelText.Required(templateName, nameof(templateName));
        TemplateFileId = CleanFileId(templateFileId);
        VariableSchemaJson = BarcodeLabelText.Required(variableSchemaJson, nameof(variableSchemaJson));
        Status = BarcodeLabelText.Supported(status, SupportedStatuses, nameof(status));
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string CleanFileId(string templateFileId)
    {
        var value = BarcodeLabelText.Required(templateFileId, nameof(templateFileId));
        if (value.Contains("objectKey", StringComparison.OrdinalIgnoreCase) || value.Contains("object_key", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Template file reference must be a FileStorage file id.", nameof(templateFileId));
        }

        return value;
    }
}
