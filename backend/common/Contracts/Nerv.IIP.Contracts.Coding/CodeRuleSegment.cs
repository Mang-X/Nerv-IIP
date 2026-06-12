namespace Nerv.IIP.Contracts.Coding;

public sealed record CodeRuleSegment
{
    public required SegmentType Type { get; init; }

    public string? Value { get; init; }

    public string? Format { get; init; }

    public int Width { get; init; } = 6;

    public int Start { get; init; } = 1;

    public char PadChar { get; init; } = '0';

    public ResetPeriod Reset { get; init; } = ResetPeriod.None;

    public string? Source { get; init; }

    public FieldTransform Transform { get; init; } = FieldTransform.None;

    public int? MaxLength { get; init; }

    public bool Required { get; init; } = true;

    public string? Algorithm { get; init; }

    public static CodeRuleSegment ConstantOf(string value) => new() { Type = SegmentType.Constant, Value = value };

    public static CodeRuleSegment DateOf(string format) => new() { Type = SegmentType.Date, Format = format };

    public static CodeRuleSegment SequenceOf(int width, ResetPeriod reset = ResetPeriod.None, int start = 1)
        => new() { Type = SegmentType.Sequence, Width = width, Reset = reset, Start = start };

    public static CodeRuleSegment FieldOf(
        string source,
        FieldTransform transform = FieldTransform.None,
        int? maxLength = null,
        bool required = true)
        => new()
        {
            Type = SegmentType.Field,
            Source = source,
            Transform = transform,
            MaxLength = maxLength,
            Required = required,
        };

    public static CodeRuleSegment ChecksumOf(string algorithm) => new() { Type = SegmentType.Checksum, Algorithm = algorithm };
}
