using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;
using Nerv.IIP.Contracts.Quality;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualitySpcAnalysisTests
{
    [Fact]
    public async Task Control_chart_projection_detects_increasing_trend_for_sku_characteristic_and_work_center()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = NewVariablePlan("IQP-SPC-TREND-001", lowerSpecLimit: 9m, upperSpecLimit: 12m);
        dbContext.InspectionPlans.Add(plan);
        AddMeasurements(dbContext, plan, [10.0m, 10.1m, 10.2m, 10.3m, 10.4m, 10.5m, 10.6m, 10.7m, 10.8m, 10.9m, 11.0m, 11.1m]);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new QuerySpcControlChartQueryHandler(dbContext).Handle(
            new QuerySpcControlChartQuery("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 2, 50),
            CancellationToken.None);

        Assert.Equal("SKU-RM-1000", response.SkuCode);
        Assert.Equal("length", response.CharacteristicCode);
        Assert.Equal("WC-MIX-01", response.WorkCenterId);
        Assert.Equal(12, response.DataPoints.Count);
        Assert.Equal(6, response.Subgroups.Count);
        Assert.False(response.ControlLimits.Locked);
        Assert.Contains(response.RuleViolations, x => x.Rule == QualitySpcRuleCodes.TrendIncreasing);
    }

    [Fact]
    public async Task Process_capability_matches_manual_cp_cpk_sample()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = NewVariablePlan("IQP-SPC-CPK-001", lowerSpecLimit: 8m, upperSpecLimit: 12m);
        dbContext.InspectionPlans.Add(plan);
        AddMeasurements(dbContext, plan, [9m, 10m, 11m]);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new QueryProcessCapabilityQueryHandler(dbContext).Handle(
            new QueryProcessCapabilityQuery("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 50),
            CancellationToken.None);

        Assert.Equal(3, response.SampleCount);
        Assert.Equal(10m, response.Mean);
        Assert.Equal(1m, response.StandardDeviation);
        Assert.Equal(0.67m, Math.Round(response.Cp!.Value, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(0.67m, Math.Round(response.Cpk!.Value, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public async Task Evaluate_control_chart_publishes_quality_spc_alert_event_for_detected_trend()
    {
        await using var provider = CreateInMemoryMediatorProvider();
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var plan = NewVariablePlan("IQP-SPC-ALERT-001", lowerSpecLimit: 9m, upperSpecLimit: 12m);
            dbContext.InspectionPlans.Add(plan);
            AddMeasurements(dbContext, plan, [10.0m, 10.1m, 10.2m, 10.3m, 10.4m, 10.5m, 10.6m, 10.7m, 10.8m, 10.9m, 11.0m, 11.1m]);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(
                new EvaluateSpcControlChartCommand("org-001", "env-dev", "SKU-RM-1000", "length", "WC-MIX-01", 2, 50),
                CancellationToken.None);
        }

        var publisher = provider.GetRequiredService<RecordingIntegrationEventPublisher>();
        var alert = Assert.Single(publisher.Published.OfType<SpcAlertRaisedIntegrationEvent>());
        Assert.Equal(QualityIntegrationEventTypes.SpcAlertRaised, alert.EventType);
        Assert.Equal("quality-spc-alert:org-001:env-dev:SKU-RM-1000:length:WC-MIX-01", alert.Payload.AlertKey);
        Assert.Contains(QualitySpcRuleCodes.TrendIncreasing, alert.Payload.RuleCodes);
        Assert.Equal("quality-spc-alert", alert.Payload.ResourceType);
    }

    private static InspectionPlan NewVariablePlan(string planCode, decimal lowerSpecLimit, decimal upperSpecLimit)
    {
        var plan = InspectionPlan.Create(
            "org-001",
            "env-dev",
            planCode,
            "operation",
            "SKU-RM-1000",
            null,
            "WC-MIX-01",
            null,
            "mes-operation");
        plan.AddCharacteristic(
            "length",
            "Length",
            "caliper",
            "major",
            required: true,
            "subgroup-2",
            InspectionCharacteristicTypes.Variable,
            nominalValue: 10m,
            lowerSpecLimit,
            upperSpecLimit,
            "mm",
            samplingPlan: null);
        plan.Activate();
        return plan;
    }

    private static void AddMeasurements(ApplicationDbContext dbContext, InspectionPlan plan, IReadOnlyCollection<decimal> measurements)
    {
        var index = 0;
        foreach (var measurement in measurements)
        {
            index++;
            dbContext.InspectionRecords.Add(InspectionRecord.CreateFromPlan(
                plan,
                "operation",
                "mes-operation",
                $"WO-SPC-{index:000}",
                "SKU-RM-1000",
                1m,
                null,
                null,
                null,
                [InspectionResultLineInput.Measure("length", measurement, "mm", [])],
                measurement is < 9m or > 12m ? "out-of-specification" : null,
                []));
        }
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"quality-spc-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateInMemoryMediatorProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"quality-spc-uow-{Guid.NewGuid():N}";
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly)
                .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<IQualityIntegrationEventContextAccessor, FixedQualityIntegrationEventContextAccessor>();
        services.AddSingleton<RecordingIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingIntegrationEventPublisher>());
        return services.BuildServiceProvider();
    }

    private sealed class FixedQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext()
        {
            return new QualityIntegrationEventContext(
                "corr-spc-001",
                "cause-spc-001",
                "system:business-quality");
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }
}
