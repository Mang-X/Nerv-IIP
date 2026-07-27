using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated;

public sealed class SimulatedScenarioEvaluator(SimulatedConnectorOptions options)
{
    public SimulatedScenarioSample Evaluate(
        SimulatedTagProfile tag,
        DateTimeOffset observedAtUtc,
        long cycle,
        decimal? controlledValue = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (cycle < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cycle));
        }

        var phase = tag.AlarmScenarioEnabled
            ? ResolvePhase(observedAtUtc, tag.PhaseOffset)
            : (Name: "normal", Progress: 0m);
        var baseline = DeterministicBaseline(tag, cycle);
        var value = controlledValue ?? phase.Name switch
        {
            "degrading" => Interpolate(baseline, tag.AlarmValue, phase.Progress),
            "alarm" => tag.AlarmValue,
            "recovered" => Interpolate(tag.AlarmValue, baseline, phase.Progress),
            _ => baseline
        };
        return new SimulatedScenarioSample(
            phase.Name,
            decimal.Round(value, 4, MidpointRounding.AwayFromZero),
            $"simulated:{tag.ConnectorId}:{tag.DeviceAssetId}:{tag.TagKey}:{cycle.ToString(CultureInfo.InvariantCulture)}",
            observedAtUtc);
    }

    private (string Name, decimal Progress) ResolvePhase(
        DateTimeOffset observedAtUtc,
        TimeSpan offset)
    {
        var periodTicks = options.Phases.Period.Ticks;
        var elapsedTicks = (observedAtUtc - options.EpochUtc + offset).Ticks % periodTicks;
        if (elapsedTicks < 0)
        {
            elapsedTicks += periodTicks;
        }

        if (elapsedTicks < options.Phases.Normal.Ticks)
        {
            return ("normal", Progress(elapsedTicks, options.Phases.Normal.Ticks));
        }

        elapsedTicks -= options.Phases.Normal.Ticks;
        if (elapsedTicks < options.Phases.Degrading.Ticks)
        {
            return ("degrading", Progress(elapsedTicks, options.Phases.Degrading.Ticks));
        }

        elapsedTicks -= options.Phases.Degrading.Ticks;
        if (elapsedTicks < options.Phases.Alarm.Ticks)
        {
            return ("alarm", Progress(elapsedTicks, options.Phases.Alarm.Ticks));
        }

        elapsedTicks -= options.Phases.Alarm.Ticks;
        return ("recovered", Progress(elapsedTicks, options.Phases.Recovered.Ticks));
    }

    private decimal DeterministicBaseline(SimulatedTagProfile tag, long cycle)
    {
        var identity = string.Join(
            '\u001f',
            options.Seed.ToString(CultureInfo.InvariantCulture),
            tag.ConnectorId,
            tag.DeviceAssetId,
            tag.TagKey,
            cycle.ToString(CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(hash) / (decimal)uint.MaxValue;
        return tag.NormalMinimum + ((tag.NormalMaximum - tag.NormalMinimum) * fraction);
    }

    private static decimal Progress(long elapsedTicks, long durationTicks) =>
        durationTicks == 0 ? 1m : elapsedTicks / (decimal)durationTicks;

    private static decimal Interpolate(decimal from, decimal to, decimal progress) =>
        from + ((to - from) * progress);
}

public sealed record SimulatedScenarioSample(
    string Phase,
    decimal Value,
    string SourceSequence,
    DateTimeOffset ObservedAtUtc);
