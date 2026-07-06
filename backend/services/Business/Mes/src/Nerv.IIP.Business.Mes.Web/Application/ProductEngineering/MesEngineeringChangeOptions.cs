namespace Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;

public sealed class MesEngineeringChangeOptions
{
    public MesEngineeringChangeNotStartedPolicy NotStartedPolicy { get; init; } = MesEngineeringChangeNotStartedPolicy.BlockForManualConfirmation;
}

public enum MesEngineeringChangeNotStartedPolicy
{
    BlockForManualConfirmation = 0,
    AutoRebind = 1,
}
