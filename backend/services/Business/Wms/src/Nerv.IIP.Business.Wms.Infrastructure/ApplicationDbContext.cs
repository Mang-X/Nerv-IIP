using MediatR;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;

namespace Nerv.IIP.Business.Wms.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator), IPostgreSqlCapDataStorage
{
    public DbSet<InboundOrder> InboundOrders => Set<InboundOrder>();
    public DbSet<BackorderOrder> BackorderOrders => Set<BackorderOrder>();
    public DbSet<InboundOrderLine> InboundOrderLines => Set<InboundOrderLine>();
    public DbSet<OutboundOrder> OutboundOrders => Set<OutboundOrder>();
    public DbSet<OutboundOrderLine> OutboundOrderLines => Set<OutboundOrderLine>();
    public DbSet<WarehouseTask> WarehouseTasks => Set<WarehouseTask>();
    public DbSet<SupplierReturnRequest> SupplierReturnRequests => Set<SupplierReturnRequest>();
    public DbSet<CountExecution> CountExecutions => Set<CountExecution>();
    public DbSet<WcsTask> WcsTasks => Set<WcsTask>();
    public DbSet<WcsDispatchCircuit> WcsDispatchCircuits => Set<WcsDispatchCircuit>();
    public DbSet<InventoryMovementRequest> InventoryMovementRequests => Set<InventoryMovementRequest>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(WmsFacts.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.ConfigureIntegrationEventDeadLetters();
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ConfigureStronglyTypedIdValueConverter(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        return ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicateAsync<ProcessedIntegrationEvent>(
            this,
            token => base.SaveChangesAsync(acceptAllChangesOnSuccess, token),
            cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return ProcessedIntegrationEventInbox.SaveChangesOrIgnoreDuplicate<ProcessedIntegrationEvent>(
            this,
            () => base.SaveChanges(acceptAllChangesOnSuccess));
    }
}
