using Microsoft.EntityFrameworkCore.ChangeTracking;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpInputChangeAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure;

internal static class MrpInputChangeCollector
{
    public static void Record(ApplicationDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var occurredAtUtc = DateTimeOffset.UtcNow;
        foreach (var entry in db.ChangeTracker.Entries<DemandSource>().Where(DemandChanged).ToArray())
        {
            var source = entry.Entity;
            var before = entry.State != EntityState.Added;
            var after = entry.State != EntityState.Deleted;
            var oldDate = before ? entry.Property(x => x.DueDate).OriginalValue : (DateOnly?)null;
            var newDate = after ? source.DueDate : (DateOnly?)null;
            db.MrpInputChanges.Add(MrpInputChange.Record(source.OrganizationId, source.EnvironmentId, "demand", source.DemandType,
                source.SourceReference, source.SourceLineReference, occurredAtUtc, Operation(entry.State),
                oldDate, oldDate, before && entry.Property(x => x.SourceStatus).OriginalValue == "active"
                    && entry.Property(x => x.Quantity).OriginalValue > 0m && source.DemandType != "forecast",
                newDate, newDate, after && source.SourceStatus == "active" && source.Quantity > 0m
                    && source.DemandType != "forecast"));
        }

        foreach (var entry in db.ChangeTracker.Entries<ForecastInput>().Where(ForecastChanged).ToArray())
        {
            var source = entry.Entity;
            var before = entry.State != EntityState.Added;
            var after = entry.State != EntityState.Deleted;
            db.MrpInputChanges.Add(MrpInputChange.Record(source.OrganizationId, source.EnvironmentId, "forecast", null,
                source.ForecastReference, "", occurredAtUtc, Operation(entry.State),
                before ? entry.Property(x => x.PeriodStartDate).OriginalValue : null,
                before ? entry.Property(x => x.PeriodEndDate).OriginalValue : null, before,
                after ? source.PeriodStartDate : null, after ? source.PeriodEndDate : null, after));
        }

        foreach (var entry in db.ChangeTracker.Entries<MasterProductionSchedule>().Where(MpsChanged).ToArray())
        {
            var source = entry.Entity;
            var before = entry.State != EntityState.Added;
            var after = entry.State != EntityState.Deleted;
            var oldDate = before ? entry.Property(x => x.BucketDate).OriginalValue : (DateOnly?)null;
            var newDate = after ? source.BucketDate : (DateOnly?)null;
            db.MrpInputChanges.Add(MrpInputChange.Record(source.OrganizationId, source.EnvironmentId, "mps", null,
                $"MPS:{source.Id}", "", occurredAtUtc, Operation(entry.State),
                oldDate, oldDate, before && entry.Property(x => x.Status).OriginalValue == MasterProductionScheduleStatus.Released,
                newDate, newDate, after && source.Status == MasterProductionScheduleStatus.Released));
        }
    }

    private static bool DemandChanged(EntityEntry<DemandSource> entry) =>
        entry.State is EntityState.Added or EntityState.Deleted ||
        entry.State == EntityState.Modified && (
            Changed(entry, x => x.Quantity) || Changed(entry, x => x.DueDate) ||
            Changed(entry, x => x.SourceStatus) || Changed(entry, x => x.SkuCode) ||
            Changed(entry, x => x.UomCode) || Changed(entry, x => x.SiteCode));

    private static bool ForecastChanged(EntityEntry<ForecastInput> entry) =>
        entry.State is EntityState.Added or EntityState.Deleted ||
        entry.State == EntityState.Modified && (
            Changed(entry, x => x.PeriodStartDate) || Changed(entry, x => x.PeriodEndDate) ||
            Changed(entry, x => x.Quantity) || Changed(entry, x => x.BackwardConsumptionDays) ||
            Changed(entry, x => x.ForwardConsumptionDays) || Changed(entry, x => x.SkuCode) ||
            Changed(entry, x => x.UomCode) || Changed(entry, x => x.SiteCode));

    private static bool MpsChanged(EntityEntry<MasterProductionSchedule> entry) =>
        entry.State is EntityState.Added or EntityState.Deleted ||
        entry.State == EntityState.Modified && (
            Changed(entry, x => x.Status) || Changed(entry, x => x.BucketDate) ||
            Changed(entry, x => x.Quantity) || Changed(entry, x => x.SkuCode) ||
            Changed(entry, x => x.UomCode) || Changed(entry, x => x.SiteCode));

    private static bool Changed<TEntity, TValue>(EntityEntry<TEntity> entry,
        System.Linq.Expressions.Expression<Func<TEntity, TValue>> property) where TEntity : class =>
        entry.Property(property).IsModified;

    private static MrpInputChangeOperation Operation(EntityState state) => state switch
    {
        EntityState.Added => MrpInputChangeOperation.Created,
        EntityState.Deleted => MrpInputChangeOperation.Deleted,
        _ => MrpInputChangeOperation.Updated,
    };
}
