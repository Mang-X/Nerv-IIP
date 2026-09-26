using Nerv.IIP.Business.ProductEngineering.Web.Application.Scheduling;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

internal sealed class FixedBusinessDateProvider(DateOnly businessDate) : IProductEngineeringBusinessDateProvider
{
    public DateOnly GetBusinessDate() => businessDate;
}
