namespace Nerv.IIP.Contracts.Coding;

public enum SegmentType
{
    Constant,
    Date,
    Sequence,
    Field,
    Checksum,
}

public enum ResetPeriod
{
    None,
    Day,
    Month,
    Year,
}

public enum FieldTransform
{
    None,
    Upper,
    Lower,
}

[Flags]
public enum ScopeDimension
{
    Organization = 1,
    Environment = 2,
    Site = 4,
}
