using Microsoft.AspNetCore.Hosting;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

internal static class BusinessGatewayTestServiceBaseUrls
{
    public static void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Iam:BaseUrl", "http://iam.local");
        builder.UseSetting("MasterData:BaseUrl", "http://master-data.local");
        builder.UseSetting("Inventory:BaseUrl", "http://inventory.local");
        builder.UseSetting("Quality:BaseUrl", "http://quality.local");
        builder.UseSetting("ProductEngineering:BaseUrl", "http://engineering.local");
        builder.UseSetting("DemandPlanning:BaseUrl", "http://planning.local");
        builder.UseSetting("Erp:BaseUrl", "http://erp.local");
        builder.UseSetting("Mes:BaseUrl", "http://mes.local");
    }
}
