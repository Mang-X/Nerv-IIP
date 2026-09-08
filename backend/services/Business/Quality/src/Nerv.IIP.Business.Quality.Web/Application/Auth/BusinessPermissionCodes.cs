using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.Business.Quality.Web.Application.Auth;

public static class BusinessPermissionCodes
{
    public const string QualityInspectionPlansManage = NervIipPermissionCodes.QualityInspectionPlansManage;
    public const string QualityInspectionRecordsCreate = NervIipPermissionCodes.QualityInspectionRecordsCreate;
    public const string QualityInspectionRecordsRead = NervIipPermissionCodes.QualityInspectionRecordsRead;
    public const string QualityMeasuringDevicesManage = "business.quality.measuring-devices.manage";
    public const string QualityMeasuringDevicesRead = "business.quality.measuring-devices.read";
    public const string QualitySpcManage = "business.quality.spc.manage";
    public const string QualityNcrRead = NervIipPermissionCodes.QualityNcrRead;
    public const string QualityNcrManage = NervIipPermissionCodes.QualityNcrManage;
}
