using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskActionReceiptAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsSchemaConventionTests
{
    [Fact]
    public void Runtime_PostgreSQL_profile_configures_migrations_history_schema()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddWmsPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");

        using var fixture = new SchemaFixture(services.BuildServiceProvider());
        var failures = SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, WmsFacts.ServiceName, WmsFacts.Schema);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Wms_schema_metadata_follows_database_conventions_and_does_not_own_stock_balance_columns()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(InboundOrder),
            typeof(InboundOrderLine),
            typeof(OutboundOrder),
            typeof(OutboundOrderLine),
            typeof(WarehouseTask),
            typeof(WarehouseTaskActionReceipt),
            typeof(CountExecution),
            typeof(WcsTask),
            typeof(InventoryMovementRequest),
            typeof(SupplierReturnRequest),
        };
        var failures = new List<string>();

        Assert.Equal(WmsFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, WmsFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, WmsFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, WmsFacts.ServiceName, WmsFacts.Schema));
        failures.AddRange(NoStockBalanceColumns(fixture.DbContext));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Wms_assignment_task_lifecycle_and_action_receipt_metadata_are_persisted_and_indexed()
    {
        using var fixture = CreateFixture();
        var model = fixture.DbContext.GetService<IDesignTimeModel>().Model;

        Assert.NotNull(fixture.DbContext.WarehouseTaskActionReceipts);
        var warehouseTask = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(WarehouseTask)));
        Assert.True(warehouseTask.FindProperty(nameof(WarehouseTask.Version))!.IsConcurrencyToken);
        Assert.Contains(
            warehouseTask.GetIndexes(),
            index => IndexMatches(
                index,
                nameof(WarehouseTask.OrganizationId),
                nameof(WarehouseTask.EnvironmentId),
                nameof(WarehouseTask.TaskType),
                nameof(WarehouseTask.Status),
                nameof(WarehouseTask.SiteCode),
                nameof(WarehouseTask.AssignedOperatorUserId),
                nameof(WarehouseTask.CreatedAtUtc)));
        Assert.Contains(
            warehouseTask.GetIndexes(),
            index => IndexMatches(
                index,
                nameof(WarehouseTask.OrganizationId),
                nameof(WarehouseTask.EnvironmentId),
                nameof(WarehouseTask.TaskType),
                nameof(WarehouseTask.Status),
                nameof(WarehouseTask.SiteCode),
                nameof(WarehouseTask.AssignedPoolCode),
                nameof(WarehouseTask.CreatedAtUtc)));
        var wcsTask = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(WcsTask)));
        Assert.Contains(
            wcsTask.GetIndexes(),
            index => index.IsUnique
                && IndexMatches(index, nameof(WcsTask.WarehouseTaskId)));

        foreach (var aggregateType in new[] { typeof(InboundOrder), typeof(OutboundOrder), typeof(CountExecution) })
        {
            var aggregate = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(aggregateType));
            Assert.True(aggregate.FindProperty("AssignedOperatorUserId")!.IsNullable);
            Assert.True(aggregate.FindProperty("AssignedPoolCode")!.IsNullable);
        }

        var receipt = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(WarehouseTaskActionReceipt)));
        Assert.Contains(
            receipt.GetIndexes(),
            index => index.IsUnique
                && IndexMatches(
                    index,
                    nameof(WarehouseTaskActionReceipt.OrganizationId),
                    nameof(WarehouseTaskActionReceipt.EnvironmentId),
                    nameof(WarehouseTaskActionReceipt.WarehouseTaskId),
                    nameof(WarehouseTaskActionReceipt.Action),
                    nameof(WarehouseTaskActionReceipt.IdempotencyKey)));
    }

    /// <summary>
    /// <see cref="WmsOperationalCodePolicy"/> 里每一个 <c>*ColumnMaxLength</c> 常量都必须绑定到一列真实存在的列，
    /// 且取值与 EF 模型一致（#3228）。**枚举从类型系统来**：反射拿到该类型上全部同名后缀的常量，
    /// 缺绑定即红——新加一个列宽常量却不说它是哪一列，不会静默漏过去。
    /// </summary>
    [Fact]
    public void Operational_code_policy_column_widths_match_the_ef_model()
    {
        var bindings = new Dictionary<string, (Type Entity, string Property)>(StringComparer.Ordinal)
        {
            [nameof(WmsOperationalCodePolicy.OutboundOrderNoColumnMaxLength)] = (typeof(OutboundOrder), nameof(OutboundOrder.OutboundOrderNo)),
            [nameof(WmsOperationalCodePolicy.SupplierReturnNoColumnMaxLength)] = (typeof(SupplierReturnRequest), nameof(SupplierReturnRequest.SupplierReturnNo)),
            [nameof(WmsOperationalCodePolicy.BackorderOrderNoColumnMaxLength)] = (typeof(BackorderOrder), nameof(BackorderOrder.BackorderOrderNo)),
            [nameof(WmsOperationalCodePolicy.WarehouseTaskNoColumnMaxLength)] = (typeof(WarehouseTask), nameof(WarehouseTask.TaskNo)),
        };

        var declaredConstants = typeof(WmsOperationalCodePolicy)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(int) && field.Name.EndsWith("ColumnMaxLength", StringComparison.Ordinal))
            .ToArray();

        using var fixture = CreateFixture();
        var model = fixture.DbContext.GetService<IDesignTimeModel>().Model;
        var failures = new List<string>();

        foreach (var constant in declaredConstants)
        {
            if (!bindings.TryGetValue(constant.Name, out var binding))
            {
                failures.Add($"WmsOperationalCodePolicy.{constant.Name} 没有绑定到任何真实列，无法核对列宽。");
                continue;
            }

            var property = model.FindEntityType(binding.Entity)?.FindProperty(binding.Property);
            if (property is null)
            {
                failures.Add($"{binding.Entity.Name}.{binding.Property} 不在 EF 模型里。");
                continue;
            }

            var declared = (int)constant.GetValue(null)!;
            if (property.GetMaxLength() != declared)
            {
                failures.Add(
                    $"WmsOperationalCodePolicy.{constant.Name}={declared} 与 {property.DeclaringType.GetTableName()}.{property.GetColumnName()} 的列宽 {property.GetMaxLength()} 不一致。");
            }
        }

        foreach (var orphan in bindings.Keys.Where(name => declaredConstants.All(constant => constant.Name != name)))
        {
            failures.Add($"绑定表里的 {orphan} 已不是 WmsOperationalCodePolicy 上的列宽常量。");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// 退供单号同时落两列，其构造上界必须取两列宽的最小值——把它读成「自己那一列」的宽度，
    /// 正是 #3228 的缺陷本体。
    /// </summary>
    [Fact]
    public void Supplier_return_no_bound_is_the_minimum_of_every_column_that_carries_it()
    {
        Assert.Equal(
            Math.Min(
                WmsOperationalCodePolicy.SupplierReturnNoColumnMaxLength,
                WmsOperationalCodePolicy.OutboundOrderNoColumnMaxLength),
            WmsOperationalCodePolicy.SupplierReturnNoMaxLength);
    }

    private static IEnumerable<string> NoStockBalanceColumns(ApplicationDbContext dbContext)
    {
        var forbiddenFragments = new[] { "on_hand", "available", "stock_balance" };
        return dbContext.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => $"{entity.GetTableName()}.{property.GetColumnName()}"))
            .Where(name => forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .Select(name => $"WMS must not own stock balance column '{name}'.")
            .ToArray();
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddWmsPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");
        return new SchemaFixture(services.BuildServiceProvider());
    }

    private static bool IndexMatches(IIndex index, params string[] propertyNames) =>
        index.Properties.Select(property => property.Name).SequenceEqual(propertyNames, StringComparer.Ordinal);

    private sealed class SchemaFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public SchemaFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ApplicationDbContext DbContext { get; }

        public void Dispose()
        {
            DbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }
}
