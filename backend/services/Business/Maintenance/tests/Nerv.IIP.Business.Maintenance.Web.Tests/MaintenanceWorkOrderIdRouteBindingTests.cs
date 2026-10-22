using System.Net.Http.Headers;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #3902 审核阻断 1：网关写维修工单时请求体不再带 workOrderId，工单 ID 只放在路由里。
/// 请求体里强类型 ID 的线上形态随 FastEndpoints 序列化配置变化（独立进程是字符串形，与网关同进程托管时是对象形），
/// 路由里的 ID 与这套配置无关。这里钉住：四个写入口在请求体不带 workOrderId 时，从路由绑定到同一个工单。
/// </summary>
public sealed class MaintenanceWorkOrderIdRouteBindingTests
{
    public static TheoryData<string, string> WriteEntries => new()
    {
        { "complete", """{"result":"fixed","downtimeReasonCode":"DT-MECH","downtimeMinutes":10,"spareParts":[],"idempotencyKey":"k-complete"}""" },
        { "assignment", """{"organizationId":"org-001","environmentId":"env-dev","technicianUserId":"tech-001","actorPrincipalId":"actor-001","reason":"dispatch","idempotencyKey":"k-assign","expectedVersion":0}""" },
        { "actions", """{"organizationId":"org-001","environmentId":"env-dev","action":"accept","actorPrincipalId":"actor-001","reason":"accept","idempotencyKey":"k-accept","expectedVersion":0}""" },
        { "repair-started", """{"repairStartedAtUtc":"2026-09-28T08:00:00Z"}""" },
    };

    [Theory]
    [MemberData(nameof(WriteEntries))]
    public async Task Write_entries_bind_the_work_order_id_from_the_route_when_the_body_omits_it(string suffix, string body)
    {
        var sender = new CapturingSender();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("IndustrialTelemetry:BaseUrl", "http://industrial-telemetry.local");
            builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var workOrderId = Guid.CreateVersion7();

        await client.PostAsync(
            $"/api/business/v1/maintenance/work-orders/{workOrderId}/{suffix}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.NotNull(sender.Captured);
        var bound = sender.Captured!.GetType().GetProperty("WorkOrderId")!.GetValue(sender.Captured);
        Assert.Equal(new MaintenanceWorkOrderId(workOrderId), bound);
    }

    /// <summary>命令一送达就记下并中止：只验证绑定结果，不跑业务。</summary>
    private sealed class CapturingSender : ISender
    {
        public object? Captured { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Captured = request;
            return Task.FromException<TResponse>(new OperationCanceledException("captured"));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Captured = request;
            return Task.FromException(new OperationCanceledException("captured"));
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Captured = request;
            return Task.FromException<object?>(new OperationCanceledException("captured"));
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
