namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

internal sealed class DemandPlanningRealPostgresFactAttribute : FactAttribute
{
    public DemandPlanningRealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run DemandPlanning real PostgreSQL evidence.";
        }
    }
}
