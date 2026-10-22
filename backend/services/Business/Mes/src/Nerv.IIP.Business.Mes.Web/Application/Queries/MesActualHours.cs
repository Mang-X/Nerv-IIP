namespace Nerv.IIP.Business.Mes.Web.Application.Queries;

/// <summary>
/// 工序完成后冻结的成对累计实绩，单位为小时。
/// </summary>
public sealed record MesActualHours(decimal LaborHours, decimal MachineHours);
