using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Quality;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesDefectProductPublicationTests
{
    // #4340 PublicContract：丢失 SKU、固定首个 SKU 或读到其它组织/环境都会失败。
    // 真命令及 UoW 发布路径；EF InMemory + recording publisher，不代表 CAP/真实栈。
    [Theory]
    [InlineData("WO-A", "SKU-A", null)]
    [InlineData("WO-B", "SKU-B", "OP-B")]
    public async Task Record_defect_publishes_source_product_and_lineage_in_its_scope(
        string workOrderId, string skuCode, string? operationTaskId)
    {
        await using var provider = CreateProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.WorkOrders.AddRange(
                WorkOrder.Create("org-other", "env-dev", workOrderId, "SKU-OTHER-ORG", "PV", 5m, 10, At),
                WorkOrder.Create("org-001", "env-other", workOrderId, "SKU-OTHER-ENV", "PV", 5m, 10, At),
                WorkOrder.Create("org-001", "env-dev", "WO-A", "SKU-A", "PV-A", 5m, 10, At),
                WorkOrder.Create("org-001", "env-dev", "WO-B", "SKU-B", "PV-B", 5m, 10, At));
            db.OperationTasks.Add(OperationTask.Create("org-001", "env-dev", "WO-B", "OP-B",
                OperationTaskLifecycleStatus.Queued, 10, "WC", [], At, TimeSpan.FromHours(1), null, null, "SKU-B"));
            await db.SaveChangesAsync();
        }

        var publisher = provider.GetRequiredService<RecordingPublisher>();
        publisher.Published.Clear();
        var command = new RecordDefectCommand("org-001", "env-dev", workOrderId,
            operationTaskId, "SURFACE", 2m, At, "defect-product-001");
        MesAcceptedResponse receipt;
        await using (var scope = provider.CreateAsyncScope())
        {
            receipt = await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
        }

        var integrationEvent = Assert.IsType<DefectRaisedIntegrationEvent>(Assert.Single(publisher.Published));
        var payloadJson = JsonSerializer.SerializeToElement(integrationEvent.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(payloadJson.TryGetProperty("skuCode", out var product), "公开不良事件必须携带来源工单的 SKU");
        Assert.Equal(skuCode, product.GetString());
        Assert.Equal(receipt.ReferenceId, integrationEvent.Payload.DefectNo);
        Assert.Equal(workOrderId, integrationEvent.Payload.WorkOrderId);
        Assert.Equal(operationTaskId, integrationEvent.Payload.OperationTaskId);
        Assert.Equal("SURFACE", integrationEvent.Payload.DefectCode);
        Assert.Equal(2m, integrationEvent.Payload.Quantity);
        Assert.Equal(At, integrationEvent.Payload.RecordedAtUtc);
        Assert.Equal(("org-001", "env-dev", "quality.DefectRaised", 1, "business-mes"),
            (integrationEvent.OrganizationId, integrationEvent.EnvironmentId, integrationEvent.EventType,
                integrationEvent.EventVersion, integrationEvent.SourceService));

        await using var replayScope = provider.CreateAsyncScope();
        Assert.Equal(receipt, await replayScope.ServiceProvider.GetRequiredService<ISender>().Send(command));
        Assert.Single(publisher.Published);
        var stored = await replayScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().DefectRecords.SingleAsync();
        Assert.Equal(receipt.ReferenceId, stored.DefectNo);
        Assert.Equal(workOrderId, stored.WorkOrderId);
        Assert.Equal(operationTaskId, stored.OperationTaskId);
    }

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-12T01:00:00Z");

    private static ServiceProvider CreateProvider()
    {
        var databaseName = $"mes-defect-product-{Guid.CreateVersion7():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<MesCodingService>();
        services.AddScoped<DefectRaisedIntegrationEventConverter>();
        services.AddSingleton<RecordingPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(sp => sp.GetRequiredService<RecordingPublisher>());
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(Program).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddUnitOfWork<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }
}
