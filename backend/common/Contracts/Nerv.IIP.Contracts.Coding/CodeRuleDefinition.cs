namespace Nerv.IIP.Contracts.Coding;

public sealed record CodeRuleDefinition
{
    private static readonly HashSet<string> AllowedDateFormats =
    [
        "yyyyMMdd",
        "yyMMdd",
        "yyyyMM",
        "yyMM",
        "yyyy",
        "yy",
    ];

    public required string RuleKey { get; init; }

    public required string DisplayName { get; init; }

    public string AppliesTo { get; init; } = string.Empty;

    public ScopeDimension Scope { get; init; } = ScopeDimension.Organization | ScopeDimension.Environment;

    public required IReadOnlyList<CodeRuleSegment> Segments { get; init; }

    public bool IsActive { get; init; } = true;

    public int Version { get; init; } = 1;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RuleKey))
        {
            throw new ArgumentException("RuleKey required.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new ArgumentException($"Rule '{RuleKey}' display name required.");
        }

        if (Segments is null || Segments.Count == 0)
        {
            throw new ArgumentException($"Rule '{RuleKey}' has no segments.");
        }

        if (!Segments.Any(segment => segment.Type == SegmentType.Sequence))
        {
            throw new ArgumentException($"Rule '{RuleKey}' must contain at least one sequence segment.");
        }

        foreach (var segment in Segments)
        {
            ValidateSegment(segment);
        }
    }

    private void ValidateSegment(CodeRuleSegment segment)
    {
        switch (segment.Type)
        {
            case SegmentType.Constant when string.IsNullOrEmpty(segment.Value):
                throw new ArgumentException($"Rule '{RuleKey}' constant segment requires Value.");
            case SegmentType.Date when string.IsNullOrEmpty(segment.Format) || !AllowedDateFormats.Contains(segment.Format):
                throw new ArgumentException($"Rule '{RuleKey}' date segment format invalid: '{segment.Format}'.");
            case SegmentType.Sequence when segment.Width <= 0:
                throw new ArgumentException($"Rule '{RuleKey}' sequence width must be positive.");
            case SegmentType.Sequence when segment.Start <= 0:
                throw new ArgumentException($"Rule '{RuleKey}' sequence start must be positive.");
            case SegmentType.Field when string.IsNullOrEmpty(segment.Source):
                throw new ArgumentException($"Rule '{RuleKey}' field segment requires Source.");
            case SegmentType.Field when segment.MaxLength <= 0:
                throw new ArgumentException($"Rule '{RuleKey}' field segment max length must be positive.");
            case SegmentType.Checksum when segment.Algorithm is not ("mod10" or "mod11"):
                throw new ArgumentException($"Rule '{RuleKey}' checksum algorithm unsupported: '{segment.Algorithm}'.");
        }
    }
}
