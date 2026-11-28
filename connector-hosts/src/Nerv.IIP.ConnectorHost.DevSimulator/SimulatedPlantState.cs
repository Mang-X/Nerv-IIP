namespace Nerv.IIP.ConnectorHost.DevSimulator;

public sealed class SimulatedPlantState : BackgroundService
{
    private PlantSnapshot current = CreateSnapshot(0);

    internal PlantSnapshot Current => Volatile.Read(ref current);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var seconds = 0;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            Volatile.Write(ref current, CreateSnapshot(++seconds));
        }
    }

    internal static PlantSnapshot CreateSnapshot(int seconds)
    {
        double Wave(double center, double amplitude) =>
            center + amplitude * (0.94 * Math.Sin(seconds / 9d) + 0.06 * (Random.Shared.NextDouble() * 2 - 1));

        bool Fault(int period) => seconds >= period && seconds % period < 30;

        return new PlantSnapshot(
            [
                [.. FloatRegisters(Fault(180) ? 79 : Wave(58.5, 4.5)), .. FloatRegisters(Wave(8200, 450)),
                    .. FloatRegisters(Wave(2.2, 0.35)), (ushort)(seconds / 4)],
                [.. FloatRegisters(Fault(240) ? 5.4 : Wave(7.2, 0.4)), .. FloatRegisters(Wave(42, 3)),
                    .. FloatRegisters(Wave(19, 2))],
                [.. FloatRegisters(Wave(29.5, 1.5)), .. FloatRegisters(Wave(6.3, 0.2))]
            ],
            Fault(210) ? 18.8 : Wave(13.25, 1.25), seconds / 6, Wave(2.4, 0.3), Wave(37, 3));
    }

    private static ushort[] FloatRegisters(double value)
    {
        var bits = BitConverter.SingleToUInt32Bits((float)value);
        return [(ushort)(bits >> 16), (ushort)bits];
    }
}

internal sealed record PlantSnapshot(
    ushort[][] Registers,
    double PressForce,
    int CycleCount,
    double DampingForce,
    double OilTemperature);
