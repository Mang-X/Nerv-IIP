namespace Nerv.IIP.Contracts.Quality;

/// <summary>NCR 证据文件的业务归属，供上传门面与 Quality 权威提交边界共同消费。</summary>
public static class QualityNcrEvidence
{
    public const string FilePurpose = "quality-evidence";
    public const string OwnerService = "business-quality";
    public const string OwnerType = "nonconformance-report";
}
