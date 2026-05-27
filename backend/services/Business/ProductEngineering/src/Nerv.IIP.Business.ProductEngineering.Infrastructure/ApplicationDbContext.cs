using MediatR;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;
using Nerv.IIP.Business.ProductEngineering.Domain;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Numbering;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator), IPostgreSqlCapDataStorage
{
    public DbSet<EngineeringDocument> EngineeringDocuments => Set<EngineeringDocument>();
    public DbSet<EngineeringItem> EngineeringItems => Set<EngineeringItem>();
    public DbSet<EngineeringBom> EngineeringBoms => Set<EngineeringBom>();
    public DbSet<ManufacturingBom> ManufacturingBoms => Set<ManufacturingBom>();
    public DbSet<Routing> Routings => Set<Routing>();
    public DbSet<EngineeringChange> EngineeringChanges => Set<EngineeringChange>();
    public DbSet<ProductionVersion> ProductionVersions => Set<ProductionVersion>();
    public DbSet<NumberingCounter> NumberingCounters => Set<NumberingCounter>();
    public DbSet<NumberingIdempotencyKey> NumberingIdempotencyKeys => Set<NumberingIdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (modelBuilder is null)
        {
            throw new ArgumentNullException(nameof(modelBuilder));
        }

        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(ProductEngineeringFacts.Schema);
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
