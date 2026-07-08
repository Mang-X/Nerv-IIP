using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.SpcControlChartAggregate;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Quality;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;

public sealed record QuerySpcControlChartQuery(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize = 5,
    int Take = 125) : IQuery<SpcControlChartResponse>;

public sealed record QueryProcessCapabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int Take = 125,
    int SubgroupSize = 5) : IQuery<ProcessCapabilityResponse>;

public sealed record EvaluateSpcControlChartCommand(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize = 5,
    int Take = 125) : ICommand<SpcEvaluationResponse>;

public sealed record LockSpcControlChartCommand(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize = 5,
    int Take = 125) : ICommand<SpcControlLimitsResponse>;

public sealed record SpcControlChartResponse(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize,
    IReadOnlyCollection<SpcMeasurementPointResponse> DataPoints,
    IReadOnlyCollection<SpcSubgroupResponse> Subgroups,
    SpcControlLimitsResponse ControlLimits,
    IReadOnlyCollection<SpcRuleViolationResponse> RuleViolations);

public sealed record SpcMeasurementPointResponse(
    string InspectionRecordId,
    string SourceDocumentId,
    DateTimeOffset MeasuredAtUtc,
    decimal MeasuredValue,
    string? UnitCode);

public sealed record SpcSubgroupResponse(
    int Index,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    decimal Xbar,
    decimal Range);

public sealed record SpcControlLimitsResponse(
    decimal CenterLine,
    decimal AverageRange,
    decimal XbarUpperControlLimit,
    decimal XbarLowerControlLimit,
    decimal RangeUpperControlLimit,
    decimal RangeLowerControlLimit,
    bool Locked,
    DateTimeOffset CalculatedAtUtc);

public sealed record SpcRuleViolationResponse(
    string Rule,
    int StartSubgroupIndex,
    int EndSubgroupIndex,
    string Message);

public sealed record ProcessCapabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SampleCount,
    decimal Mean,
    decimal StandardDeviation,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    decimal? Cp,
    decimal? Cpk);

public sealed record SpcEvaluationResponse(
    bool AlertRaised,
    IReadOnlyCollection<SpcRuleViolationResponse> RuleViolations);

public sealed class QuerySpcControlChartQueryValidator : AbstractValidator<QuerySpcControlChartQuery>
{
    public QuerySpcControlChartQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CharacteristicCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SubgroupSize).InclusiveBetween(2, 10);
        RuleFor(x => x.Take).InclusiveBetween(2, 500);
    }
}

public sealed class QueryProcessCapabilityQueryValidator : AbstractValidator<QueryProcessCapabilityQuery>
{
    public QueryProcessCapabilityQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CharacteristicCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SubgroupSize).InclusiveBetween(2, 10);
        RuleFor(x => x.Take).InclusiveBetween(2, 500);
    }
}

public sealed class QuerySpcControlChartQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QuerySpcControlChartQuery, SpcControlChartResponse>
{
    public async Task<SpcControlChartResponse> Handle(QuerySpcControlChartQuery request, CancellationToken cancellationToken)
    {
        var points = await SpcDataProjection.LoadPointsAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.CharacteristicCode,
            request.WorkCenterId,
            request.Take,
            cancellationToken);
        var subgroups = SpcCalculation.BuildSubgroups(points, request.SubgroupSize);
        var locked = await dbContext.SpcControlCharts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.SkuCode == request.SkuCode
            && x.CharacteristicCode == request.CharacteristicCode.Trim().ToLowerInvariant()
            && x.WorkCenterId == request.WorkCenterId
            && x.SubgroupSize == request.SubgroupSize
            && x.Locked,
            cancellationToken);
        var limits = locked is null
            ? SpcCalculation.CalculateLimits(subgroups, request.SubgroupSize, locked: false)
            : SpcCalculation.ToLimits(locked);
        var violations = SpcCalculation.DetectViolations(subgroups, limits);

        return new SpcControlChartResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.CharacteristicCode.ToLowerInvariant(),
            request.WorkCenterId,
            request.SubgroupSize,
            points,
            subgroups,
            limits,
            violations);
    }
}

public sealed class QueryProcessCapabilityQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryProcessCapabilityQuery, ProcessCapabilityResponse>
{
    public async Task<ProcessCapabilityResponse> Handle(QueryProcessCapabilityQuery request, CancellationToken cancellationToken)
    {
        var points = await SpcDataProjection.LoadPointsAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.CharacteristicCode,
            request.WorkCenterId,
            request.Take,
            cancellationToken);
        var spec = await SpcDataProjection.LoadSpecificationAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.CharacteristicCode,
            request.WorkCenterId,
            cancellationToken);
        var values = points.Select(x => x.MeasuredValue).ToArray();
        var mean = SpcCalculation.Mean(values);
        var subgroups = SpcCalculation.BuildSubgroups(points, request.SubgroupSize);
        var standardDeviation = SpcCalculation.EstimateWithinSubgroupStandardDeviation(subgroups, request.SubgroupSize);
        var cp = spec.LowerSpecLimit.HasValue && spec.UpperSpecLimit.HasValue && standardDeviation > 0
            ? (spec.UpperSpecLimit.Value - spec.LowerSpecLimit.Value) / (6m * standardDeviation)
            : (decimal?)null;
        decimal? cpk = standardDeviation > 0
            ? SpcCalculation.CalculateCpk(mean, standardDeviation, spec.LowerSpecLimit, spec.UpperSpecLimit)
            : null;

        return new ProcessCapabilityResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.CharacteristicCode.ToLowerInvariant(),
            request.WorkCenterId,
            values.Length,
            SpcCalculation.Round(mean),
            SpcCalculation.Round(standardDeviation),
            spec.LowerSpecLimit,
            spec.UpperSpecLimit,
            cp.HasValue ? SpcCalculation.Round(cp.Value) : null,
            cpk.HasValue ? SpcCalculation.Round(cpk.Value) : null);
    }
}

public sealed class EvaluateSpcControlChartCommandHandler(
    ISender sender,
    IIntegrationEventPublisher publisher,
    IQualityIntegrationEventContextAccessor contextAccessor)
    : ICommandHandler<EvaluateSpcControlChartCommand, SpcEvaluationResponse>
{
    public async Task<SpcEvaluationResponse> Handle(EvaluateSpcControlChartCommand request, CancellationToken cancellationToken)
    {
        SpcControlChartResponse chart;
        try
        {
            chart = await sender.Send(new QuerySpcControlChartQuery(
                request.OrganizationId,
                request.EnvironmentId,
                request.SkuCode,
                request.CharacteristicCode,
                request.WorkCenterId,
                request.SubgroupSize,
                request.Take), cancellationToken);
        }
        catch (KnownException exception) when (string.Equals(exception.Message, SpcCalculation.NoCompleteSubgroupMessage, StringComparison.Ordinal))
        {
            return new SpcEvaluationResponse(false, []);
        }

        if (chart.RuleViolations.Count == 0 || chart.DataPoints.Count == 0)
        {
            return new SpcEvaluationResponse(false, chart.RuleViolations);
        }

        var latest = chart.DataPoints.MaxBy(x => x.MeasuredAtUtc)!;
        var context = contextAccessor.GetContext();
        var alertKey = SpcAlertIntegrationEvents.AlertKey(chart.OrganizationId, chart.EnvironmentId, chart.SkuCode, chart.CharacteristicCode, chart.WorkCenterId);
        var integrationEvent = SpcAlertIntegrationEvents.Create(
            chart,
            latest.MeasuredAtUtc,
            alertKey,
            chart.RuleViolations.Select(x => x.Rule).Distinct(StringComparer.Ordinal).ToArray(),
            context);
        await publisher.PublishAsync(integrationEvent, cancellationToken);
        return new SpcEvaluationResponse(true, chart.RuleViolations);
    }
}

public sealed class LockSpcControlChartCommandHandler(ApplicationDbContext dbContext, ISender sender)
    : ICommandHandler<LockSpcControlChartCommand, SpcControlLimitsResponse>
{
    public async Task<SpcControlLimitsResponse> Handle(LockSpcControlChartCommand request, CancellationToken cancellationToken)
    {
        var chart = await sender.Send(new QuerySpcControlChartQuery(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.CharacteristicCode,
            request.WorkCenterId,
            request.SubgroupSize,
            request.Take), cancellationToken);
        var entity = await dbContext.SpcControlCharts.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.SkuCode == request.SkuCode
            && x.CharacteristicCode == request.CharacteristicCode.Trim().ToLowerInvariant()
            && x.WorkCenterId == request.WorkCenterId
            && x.SubgroupSize == request.SubgroupSize,
            cancellationToken);
        if (entity is null)
        {
            entity = SpcControlChart.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.SkuCode,
                request.CharacteristicCode,
                request.WorkCenterId,
                request.SubgroupSize);
            dbContext.SpcControlCharts.Add(entity);
        }

        var calculatedAtUtc = chart.ControlLimits.CalculatedAtUtc.UtcDateTime;
        entity.LockLimits(
            chart.ControlLimits.CenterLine,
            chart.ControlLimits.AverageRange,
            chart.ControlLimits.XbarUpperControlLimit,
            chart.ControlLimits.XbarLowerControlLimit,
            chart.ControlLimits.RangeUpperControlLimit,
            chart.ControlLimits.RangeLowerControlLimit,
            calculatedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return SpcCalculation.ToLimits(entity);
    }
}

internal static class SpcDataProjection
{
    public static async Task<IReadOnlyCollection<SpcMeasurementPointResponse>> LoadPointsAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string skuCode,
        string characteristicCode,
        string workCenterId,
        int take,
        CancellationToken cancellationToken)
    {
        var normalizedCharacteristic = characteristicCode.Trim().ToLowerInvariant();
        var rows = await (from record in dbContext.InspectionRecords.AsNoTracking()
                join plan in dbContext.InspectionPlans.AsNoTracking() on record.InspectionPlanId equals plan.Id
                from line in record.ResultLines
                where record.OrganizationId == organizationId
                    && record.EnvironmentId == environmentId
                    && record.SkuCode == skuCode
                    && plan.WorkCenterId == workCenterId
                    && line.CharacteristicCode == normalizedCharacteristic
                    && line.MeasuredValue.HasValue
                orderby record.CreatedAtUtc descending
                select new SpcMeasurementPointProjection(
                    record.Id.ToString(),
                    record.SourceDocumentId,
                    record.CreatedAtUtc,
                    line.MeasuredValue!.Value,
                    line.UnitCode))
            .Take(Math.Clamp(take, 2, 500))
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new SpcMeasurementPointResponse(
                x.InspectionRecordId,
                x.SourceDocumentId,
                new DateTimeOffset(DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc)),
                x.MeasuredValue,
                x.UnitCode))
            .ToArray();
    }

    public static async Task<SpcSpecification> LoadSpecificationAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string skuCode,
        string characteristicCode,
        string workCenterId,
        CancellationToken cancellationToken)
    {
        var normalizedCharacteristic = characteristicCode.Trim().ToLowerInvariant();
        var plans = await dbContext.InspectionPlans
            .AsNoTracking()
            .Include(x => x.Characteristics)
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.SkuCode == skuCode
                && x.WorkCenterId == workCenterId
                && x.Status == "active")
            .OrderByDescending(x => x.ActivatedAtUtc)
            .ToListAsync(cancellationToken);
        var characteristic = plans
            .SelectMany(x => x.Characteristics)
            .FirstOrDefault(x => x.CharacteristicCode == normalizedCharacteristic && x.CharacteristicType == InspectionCharacteristicTypes.Variable)
            ?? throw new KnownException($"Variable characteristic '{normalizedCharacteristic}' was not found for SPC capability analysis.");
        return new SpcSpecification(characteristic.LowerSpecLimit, characteristic.UpperSpecLimit);
    }
}

internal sealed record SpcSpecification(decimal? LowerSpecLimit, decimal? UpperSpecLimit);

internal sealed record SpcMeasurementPointProjection(
    string InspectionRecordId,
    string SourceDocumentId,
    DateTime CreatedAtUtc,
    decimal MeasuredValue,
    string? UnitCode);

internal static class SpcCalculation
{
    public const string NoCompleteSubgroupMessage = "SPC control chart requires at least one complete subgroup.";

    private static readonly IReadOnlyDictionary<int, XbarRConstants> Constants = new Dictionary<int, XbarRConstants>
    {
        [2] = new(1.880m, 0m, 3.267m, 1.128m),
        [3] = new(1.023m, 0m, 2.574m, 1.693m),
        [4] = new(0.729m, 0m, 2.282m, 2.059m),
        [5] = new(0.577m, 0m, 2.114m, 2.326m),
        [6] = new(0.483m, 0m, 2.004m, 2.534m),
        [7] = new(0.419m, 0.076m, 1.924m, 2.704m),
        [8] = new(0.373m, 0.136m, 1.864m, 2.847m),
        [9] = new(0.337m, 0.184m, 1.816m, 2.970m),
        [10] = new(0.308m, 0.223m, 1.777m, 3.078m),
    };

    public static IReadOnlyCollection<SpcSubgroupResponse> BuildSubgroups(
        IReadOnlyCollection<SpcMeasurementPointResponse> points,
        int subgroupSize)
    {
        return points
            .Select((point, index) => new { point, index })
            .GroupBy(x => x.index / subgroupSize)
            .Where(group => group.Count() == subgroupSize)
            .Select(group =>
            {
                var values = group.Select(x => x.point.MeasuredValue).ToArray();
                return new SpcSubgroupResponse(
                    group.Key + 1,
                    group.Min(x => x.point.MeasuredAtUtc),
                    group.Max(x => x.point.MeasuredAtUtc),
                    Round(Mean(values)),
                    Round(values.Max() - values.Min()));
            })
            .ToArray();
    }

    public static SpcControlLimitsResponse CalculateLimits(
        IReadOnlyCollection<SpcSubgroupResponse> subgroups,
        int subgroupSize,
        bool locked)
    {
        if (!Constants.TryGetValue(subgroupSize, out var constants))
        {
            throw new KnownException($"SPC Xbar-R constants are not configured for subgroup size {subgroupSize}.");
        }

        if (subgroups.Count == 0)
        {
            throw new KnownException(NoCompleteSubgroupMessage);
        }

        var centerLine = Mean(subgroups.Select(x => x.Xbar).ToArray());
        var averageRange = Mean(subgroups.Select(x => x.Range).ToArray());
        var xbarUcl = centerLine + constants.A2 * averageRange;
        var xbarLcl = centerLine - constants.A2 * averageRange;
        var rUcl = constants.D4 * averageRange;
        var rLcl = constants.D3 * averageRange;
        return new SpcControlLimitsResponse(
            Round(centerLine),
            Round(averageRange),
            Round(xbarUcl),
            Round(xbarLcl),
            Round(rUcl),
            Round(rLcl),
            locked,
            DateTimeOffset.UtcNow);
    }

    public static SpcControlLimitsResponse ToLimits(SpcControlChart chart)
    {
        return new SpcControlLimitsResponse(
            chart.CenterLine,
            chart.AverageRange,
            chart.XbarUpperControlLimit,
            chart.XbarLowerControlLimit,
            chart.RangeUpperControlLimit,
            chart.RangeLowerControlLimit,
            chart.Locked,
            new DateTimeOffset(DateTime.SpecifyKind(chart.LimitsCalculatedAtUtc ?? chart.UpdatedAtUtc, DateTimeKind.Utc)));
    }

    public static IReadOnlyCollection<SpcRuleViolationResponse> DetectViolations(
        IReadOnlyCollection<SpcSubgroupResponse> subgroups,
        SpcControlLimitsResponse limits)
    {
        var violations = new List<SpcRuleViolationResponse>();
        foreach (var subgroup in subgroups)
        {
            if (subgroup.Xbar > limits.XbarUpperControlLimit
                || subgroup.Xbar < limits.XbarLowerControlLimit
                || subgroup.Range > limits.RangeUpperControlLimit
                || subgroup.Range < limits.RangeLowerControlLimit)
            {
                violations.Add(new SpcRuleViolationResponse(
                    QualitySpcRuleCodes.BeyondControlLimit,
                    subgroup.Index,
                    subgroup.Index,
                    $"Subgroup {subgroup.Index} exceeds Xbar-R control limits."));
            }
        }

        AddConsecutiveShiftViolations(subgroups, limits.CenterLine, violations);
        AddTrendViolations(subgroups, violations);
        return violations
            .GroupBy(x => new { x.Rule, x.StartSubgroupIndex, x.EndSubgroupIndex })
            .Select(x => x.First())
            .ToArray();
    }

    public static decimal? CalculateCpk(decimal mean, decimal standardDeviation, decimal? lowerSpecLimit, decimal? upperSpecLimit)
    {
        var candidates = new List<decimal>();
        if (upperSpecLimit.HasValue)
        {
            candidates.Add((upperSpecLimit.Value - mean) / (3m * standardDeviation));
        }

        if (lowerSpecLimit.HasValue)
        {
            candidates.Add((mean - lowerSpecLimit.Value) / (3m * standardDeviation));
        }

        return candidates.Count == 0 ? null : candidates.Min();
    }

    public static decimal Mean(IReadOnlyCollection<decimal> values)
    {
        return values.Count == 0 ? 0m : values.Sum() / values.Count;
    }

    public static decimal EstimateWithinSubgroupStandardDeviation(
        IReadOnlyCollection<SpcSubgroupResponse> subgroups,
        int subgroupSize)
    {
        if (subgroups.Count == 0)
        {
            return 0m;
        }

        if (!Constants.TryGetValue(subgroupSize, out var constants))
        {
            throw new KnownException($"SPC Xbar-R constants are not configured for subgroup size {subgroupSize}.");
        }

        var averageRange = Mean(subgroups.Select(x => x.Range).ToArray());
        return constants.D2 <= 0 ? 0m : averageRange / constants.D2;
    }

    public static decimal Round(decimal value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    private static void AddConsecutiveShiftViolations(
        IReadOnlyCollection<SpcSubgroupResponse> subgroups,
        decimal centerLine,
        List<SpcRuleViolationResponse> violations)
    {
        AddPredicateRunViolation(
            subgroups,
            x => x.Xbar > centerLine,
            7,
            QualitySpcRuleCodes.ConsecutiveShiftAboveCenter,
            "Seven consecutive subgroup means are above the center line.",
            violations);
        AddPredicateRunViolation(
            subgroups,
            x => x.Xbar < centerLine,
            7,
            QualitySpcRuleCodes.ConsecutiveShiftBelowCenter,
            "Seven consecutive subgroup means are below the center line.",
            violations);
    }

    private static void AddTrendViolations(
        IReadOnlyCollection<SpcSubgroupResponse> subgroups,
        List<SpcRuleViolationResponse> violations)
    {
        AddTrendRunViolation(
            subgroups,
            (previous, current) => current.Xbar > previous.Xbar,
            QualitySpcRuleCodes.TrendIncreasing,
            "Six consecutive subgroup means are increasing.",
            violations);
        AddTrendRunViolation(
            subgroups,
            (previous, current) => current.Xbar < previous.Xbar,
            QualitySpcRuleCodes.TrendDecreasing,
            "Six consecutive subgroup means are decreasing.",
            violations);
    }

    private static void AddPredicateRunViolation(
        IReadOnlyCollection<SpcSubgroupResponse> subgroups,
        Func<SpcSubgroupResponse, bool> predicate,
        int minimumLength,
        string rule,
        string message,
        List<SpcRuleViolationResponse> violations)
    {
        var array = subgroups.OrderBy(x => x.Index).ToArray();
        var start = -1;
        for (var index = 0; index <= array.Length; index++)
        {
            var matches = index < array.Length && predicate(array[index]);
            if (matches && start < 0)
            {
                start = index;
            }

            if ((matches || start < 0) && index < array.Length)
            {
                continue;
            }

            if (start < 0)
            {
                continue;
            }

            var length = index - start;
            if (length >= minimumLength)
            {
                violations.Add(new SpcRuleViolationResponse(rule, array[start].Index, array[index - 1].Index, message));
            }

            start = -1;
        }
    }

    private static void AddTrendRunViolation(
        IReadOnlyCollection<SpcSubgroupResponse> subgroups,
        Func<SpcSubgroupResponse, SpcSubgroupResponse, bool> isTrendStep,
        string rule,
        string message,
        List<SpcRuleViolationResponse> violations)
    {
        var array = subgroups.OrderBy(x => x.Index).ToArray();
        if (array.Length < 2)
        {
            return;
        }

        var start = 0;
        for (var index = 1; index <= array.Length; index++)
        {
            var continues = index < array.Length && isTrendStep(array[index - 1], array[index]);
            if (continues)
            {
                continue;
            }

            var length = index - start;
            if (length >= 6)
            {
                violations.Add(new SpcRuleViolationResponse(rule, array[start].Index, array[index - 1].Index, message));
            }

            start = index;
        }
    }

    private sealed record XbarRConstants(decimal A2, decimal D3, decimal D4, decimal D2);
}

internal static class SpcAlertIntegrationEvents
{
    public static SpcAlertRaisedIntegrationEvent Create(
        SpcControlChartResponse chart,
        DateTimeOffset latestMeasuredAtUtc,
        string alertKey,
        IReadOnlyCollection<string> ruleCodes,
        QualityIntegrationEventContext context)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new SpcAlertRaisedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.SpcAlertRaised,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            chart.OrganizationId,
            chart.EnvironmentId,
            context.Actor,
            EventIds.Idempotency(
                alertKey,
                latestMeasuredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                string.Join(",", ruleCodes.Order(StringComparer.Ordinal))),
            new SpcAlertRaisedPayload(
                alertKey,
                "quality-spc-alert",
                chart.SkuCode,
                chart.CharacteristicCode,
                chart.WorkCenterId,
                ruleCodes,
                "warning",
                latestMeasuredAtUtc,
                $"SPC alert for {chart.SkuCode}/{chart.CharacteristicCode} at {chart.WorkCenterId}: {string.Join(", ", ruleCodes)}."));
    }

    public static string AlertKey(
        string organizationId,
        string environmentId,
        string skuCode,
        string characteristicCode,
        string workCenterId)
    {
        return $"quality-spc-alert:{organizationId}:{environmentId}:{skuCode}:{characteristicCode.ToLowerInvariant()}:{workCenterId}";
    }
}
