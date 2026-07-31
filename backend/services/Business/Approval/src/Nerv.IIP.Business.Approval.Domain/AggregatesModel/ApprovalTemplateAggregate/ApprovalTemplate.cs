using Nerv.IIP.Business.Approval.Domain.AggregatesModel;
using System.Text.Json;

namespace Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;

public partial record ApprovalTemplateId : IGuidStronglyTypedId;

public partial record ApprovalTemplateStepId : IGuidStronglyTypedId;

public sealed class ApprovalTemplate : Entity<ApprovalTemplateId>, IAggregateRoot
{
    private readonly List<ApprovalTemplateStep> steps = [];

    private ApprovalTemplate()
    {
    }

    private ApprovalTemplate(
        string organizationId,
        string environmentId,
        string templateCode,
        string documentType,
        int version,
        bool isActive,
        IEnumerable<ApprovalTemplateStepDefinition> stepDefinitions)
    {
        Id = new ApprovalTemplateId(Guid.CreateVersion7());
        OrganizationId = ApprovalText.Required(organizationId);
        EnvironmentId = ApprovalText.Required(environmentId);
        TemplateCode = ApprovalText.Required(templateCode);
        DocumentType = ApprovalText.Required(documentType);
        Version = Positive(version, nameof(version));
        IsActive = isActive;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        ReplaceSteps(stepDefinitions);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string TemplateCode { get; private set; } = string.Empty;
    public string DocumentType { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<ApprovalTemplateStep> Steps => steps;

    public static ApprovalTemplate Create(
        string organizationId,
        string environmentId,
        string templateCode,
        string documentType,
        int version,
        bool isActive,
        IEnumerable<ApprovalTemplateStepDefinition> stepDefinitions)
    {
        return new ApprovalTemplate(
            organizationId,
            environmentId,
            templateCode,
            documentType,
            version,
            isActive,
            stepDefinitions);
    }

    public void ReplaceDefinition(string documentType, int version, bool isActive, IEnumerable<ApprovalTemplateStepDefinition> stepDefinitions)
    {
        DocumentType = ApprovalText.Required(documentType);
        Version = Positive(version, nameof(version));
        IsActive = isActive;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        steps.Clear();
        ReplaceSteps(stepDefinitions);
    }

    public void EnsureActive()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("审批模板未启用，无法用于发起审批。");
        }

        if (steps.Count == 0)
        {
            throw new InvalidOperationException("审批模板至少要配置一个审批步骤。");
        }
    }

    private void ReplaceSteps(IEnumerable<ApprovalTemplateStepDefinition> stepDefinitions)
    {
        var materializedSteps = stepDefinitions
            .OrderBy(x => x.StepNo)
            .ThenBy(x => x.ApproverType, StringComparer.Ordinal)
            .ThenBy(x => x.ApproverRef, StringComparer.Ordinal)
            .ToArray();
        if (materializedSteps.Length == 0)
        {
            throw new ArgumentException("审批模板至少要配置一个审批步骤。", nameof(stepDefinitions));
        }

        var stepNos = materializedSteps.Select(x => Positive(x.StepNo, nameof(x.StepNo))).Distinct().Order().ToArray();
        if (stepNos.First() != 1 || stepNos.SequenceEqual(Enumerable.Range(1, stepNos.Length)) is false)
        {
            throw new ArgumentException("Approval template step numbers must start at 1 and be contiguous.", nameof(stepDefinitions));
        }

        foreach (var parallelCandidate in materializedSteps.GroupBy(x => x.StepNo).Where(group => group.Count() > 1))
        {
            var groupKeys = parallelCandidate.Select(x => ApprovalText.Optional(x.ParallelGroupKey)).Distinct(StringComparer.Ordinal).ToArray();
            if (groupKeys.Length != 1 || string.IsNullOrWhiteSpace(groupKeys[0]))
            {
                throw new ArgumentException("Duplicate approval step numbers require the same explicit parallel group key.", nameof(stepDefinitions));
            }

            var policies = parallelCandidate.Select(x => ApprovalCompletionPolicies.Normalize(x.CompletionPolicy)).Distinct(StringComparer.Ordinal).ToArray();
            if (policies.Length != 1)
            {
                throw new ArgumentException("Duplicate approval step numbers require the same completion policy.", nameof(stepDefinitions));
            }
        }

        foreach (var definition in materializedSteps)
        {
            steps.Add(new ApprovalTemplateStep(definition));
        }
    }

    private static int Positive(int value, string parameterName)
    {
        return value <= 0 ? throw new ArgumentOutOfRangeException(parameterName, "Value must be positive.") : value;
    }
}

public sealed class ApprovalTemplateStep : Entity<ApprovalTemplateStepId>
{
    private ApprovalTemplateStep()
    {
    }

    internal ApprovalTemplateStep(ApprovalTemplateStepDefinition definition)
    {
        Id = new ApprovalTemplateStepId(Guid.CreateVersion7());
        StepNo = definition.StepNo;
        StepName = ApprovalText.Required(definition.StepName);
        ParallelGroupKey = ApprovalText.Optional(definition.ParallelGroupKey);
        CompletionPolicy = ApprovalCompletionPolicies.Normalize(definition.CompletionPolicy);
        ConditionExpression = definition.Condition is null
            ? ApprovalText.Optional(definition.ConditionExpression)
            : ApprovalRoutingCondition.Serialize(definition.Condition);
        ApproverType = ApprovalText.RequiredLower(definition.ApproverType);
        ApproverRef = ApprovalText.Required(definition.ApproverRef);
        DueInHours = definition.DueInHours;
        if (DueInHours is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition.DueInHours), "Due hours must be positive when specified.");
        }
    }

    public ApprovalTemplateId TemplateId { get; private set; } = null!;
    public int StepNo { get; private set; }
    public string StepName { get; private set; } = string.Empty;
    public string? ParallelGroupKey { get; private set; }
    public string CompletionPolicy { get; private set; } = string.Empty;
    public string? ConditionExpression { get; private set; }
    public string ApproverType { get; private set; } = string.Empty;
    public string ApproverRef { get; private set; } = string.Empty;
    public int? DueInHours { get; private set; }
}

public sealed record ApprovalTemplateStepDefinition(
    int StepNo,
    string StepName,
    string? ParallelGroupKey,
    string ApproverType,
    string ApproverRef,
    int? DueInHours,
    string CompletionPolicy = ApprovalCompletionPolicies.All,
    string? ConditionExpression = null,
    ApprovalRoutingCondition? Condition = null);

public sealed record ApprovalRoutingCondition(
    decimal? MinimumAmount = null,
    decimal? MaximumAmount = null,
    IReadOnlyCollection<string>? DocumentTypes = null,
    IReadOnlyCollection<string>? OrganizationIds = null,
    IReadOnlyCollection<string>? DepartmentIds = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(ApprovalRoutingCondition condition)
    {
        condition.Validate();
        return JsonSerializer.Serialize(condition, SerializerOptions);
    }

    public static ApprovalRoutingCondition Deserialize(string value)
    {
        var condition = JsonSerializer.Deserialize<ApprovalRoutingCondition>(value, SerializerOptions)
            ?? throw new InvalidOperationException("结构化审批条件不能为空。");
        condition.Validate();
        return condition;
    }

    public void Validate()
    {
        if (MinimumAmount is < 0 || MaximumAmount is < 0 || (MinimumAmount.HasValue && MaximumAmount.HasValue && MinimumAmount > MaximumAmount))
        {
            throw new InvalidOperationException("审批金额条件区间非法：下限不能为负，且不能大于上限。");
        }

        ValidateDimension(DocumentTypes, nameof(DocumentTypes));
        ValidateDimension(OrganizationIds, nameof(OrganizationIds));
        ValidateDimension(DepartmentIds, nameof(DepartmentIds));
    }

    private static void ValidateDimension(IReadOnlyCollection<string>? values, string name)
    {
        if (values?.Any(string.IsNullOrWhiteSpace) is true)
        {
            throw new InvalidOperationException($"审批条件“{name}”不能包含空值。");
        }
    }
}

public static class ApprovalCompletionPolicies
{
    public const string All = "all";
    public const string Any = "any";

    /// <summary>
    /// 步骤完成策略的允许取值（all 会签 / any 或签）——**唯一权威**，应用层校验器共用这一份。
    /// 大小写不敏感：<see cref="Normalize"/> 本就先归一化成小写再落库，校验器若自己写死小写字面量
    /// 就会比领域更严，"All" 这类大小写差异被拦成没有线索的 400（同 #1313 的裁决取值坑）。
    /// </summary>
    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>([All, Any], StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? All : value.Trim().ToLowerInvariant();
        return Supported.Contains(normalized)
            ? normalized
            : throw new ArgumentException("步骤完成策略只能是 all（会签）或 any（或签）。", nameof(value));
    }
}
