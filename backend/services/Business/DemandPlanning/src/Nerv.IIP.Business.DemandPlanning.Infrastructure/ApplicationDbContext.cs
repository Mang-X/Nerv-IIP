using MediatR;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator), IPostgreSqlCapDataStorage
{
    public DbSet<DemandSource> DemandSources => Set<DemandSource>();
    public DbSet<ForecastInput> ForecastInputs => Set<ForecastInput>();
    public DbSet<MasterProductionSchedule> MasterProductionSchedules => Set<MasterProductionSchedule>();
    public DbSet<MrpRun> MrpRuns => Set<MrpRun>();
    public DbSet<PlanningSuggestion> PlanningSuggestions => Set<PlanningSuggestion>();
    public DbSet<PeggingLink> PeggingLinks => Set<PeggingLink>();
    public DbSet<CodeCounter> CodeCounters => Set<CodeCounter>();
    public DbSet<CodeIdempotencyKey> CodeIdempotencyKeys => Set<CodeIdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(DemandPlanningFacts.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ConfigureCapStorage(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ConfigureStronglyTypedIdValueConverter(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);
    }

    private static void ConfigureCapStorage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PublishedMessage>().ToTable("cap_published_messages").HasKey(x => x.Id);
        modelBuilder.Entity<ReceivedMessage>().ToTable("cap_received_messages").HasKey(x => x.Id);
        modelBuilder.Entity<CapLock>().ToTable("cap_locks").HasKey(x => x.Key);
    }
}
