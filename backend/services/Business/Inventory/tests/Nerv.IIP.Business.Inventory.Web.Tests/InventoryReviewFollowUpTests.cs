using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Auth;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.Expiry;
using Nerv.IIP.Business.Inventory.Web.Application.MasterData;
using Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryReviewFollowUpTests
{
    private static readonly DateOnly Today = new(2026, 7, 5);

    [Fact]
    public void Inventory_permission_context_allows_forwarded_internal_permission_header()
    {
        var internalPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "internal-service"),
            new Claim(ClaimTypes.Name, "internal-service"),
            new Claim("token_type", "internal_service")
        ], "InternalService"));
        var headers = new HeaderDictionary
        {
            [InventoryPermissionContext.ForwardedPermissionHeaderName] = InventoryPermissionCodes.ExpiredStockOverride
        };

        Assert.False(InventoryPermissionContext.HasPermission(
            internalPrincipal,
            new HeaderDictionary(),
            InventoryPermissionCodes.ExpiredStockOverride));
        Assert.True(InventoryPermissionContext.HasPermission(
            internalPrincipal,
            headers,
            InventoryPermissionCodes.ExpiredStockOverride));
    }

    [Fact]
    public async Task Expired_stock_blocking_uses_status_transfer_command_with_batch_dates()
    {
        await using var dbContext = CreateContext();
        await SeedLedgerAsync(dbContext, "LOT-BLOCK", new DateOnly(2026, 7, 1), 5m);
        var sender = new RecordingStatusTransferSender(dbContext);
        var service = new ExpiredStockBlockingService(
            dbContext,
            Options.Create(new ExpiredStockBlockingOptions { Enabled = true }),
            sender);

        var count = await service.BlockExpiredAvailableStockAsync(Today, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, count);
        var command = Assert.Single(sender.Commands);
        Assert.Equal("inventory-expiry", command.SourceService);
        Assert.Equal("unrestricted", command.SourceQualityStatus);
        Assert.Equal("blocked", command.TargetQualityStatus);
        Assert.Equal(new DateOnly(2026, 6, 1), command.ProductionDate);
        Assert.Equal(new DateOnly(2026, 7, 1), command.ExpiryDate);
    }

    [Fact]
    public async Task Sku_expiry_policy_provider_returns_null_when_masterdata_is_unavailable()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.InternalServerError))
        {
            BaseAddress = new Uri("http://masterdata.test")
        };
        var provider = new HttpInventorySkuExpiryPolicyProvider(httpClient, new FakeInternalServiceTokenProvider());

        var policy = await provider.GetAsync("org-001", "env-dev", "SKU-FEFO", CancellationToken.None);

        Assert.Null(policy);
    }

    private static async Task SeedLedgerAsync(ApplicationDbContext dbContext, string lotNo, DateOnly expiryDate, decimal quantity)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "LOC-A-01",
            lotNo,
            null,
            "qualified",
            "company",
            "owner-001",
            ProductionDate: expiryDate.AddDays(-30),
            ExpiryDate: expiryDate);
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            $"IN-{lotNo}",
            "LINE-001",
            $"idem-in-{lotNo}",
            "SKU-FEFO",
            "kg",
            "SITE-01",
            "LOC-A-01",
            lotNo,
            null,
            "qualified",
            "company",
            "owner-001",
            quantity,
            ProductionDate: expiryDate.AddDays(-30),
            ExpiryDate: expiryDate));
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-review-follow-up-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class RecordingStatusTransferSender(ApplicationDbContext dbContext) : ISender
    {
        public List<PostStockStatusTransferCommand> Commands { get; } = [];

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PostStockStatusTransferCommand command && typeof(TResponse) == typeof(PostStockStatusTransferResult))
            {
                Commands.Add(command);
                var result = await new PostStockStatusTransferCommandHandler(dbContext).Handle(command, cancellationToken);
                return (TResponse)(object)result;
            }

            throw new NotSupportedException($"Unsupported request type {request.GetType().Name}.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException($"Unsupported request type {typeof(TRequest).Name}.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException($"Unsupported request type {request.GetType().Name}.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(new { success = false, message = "unavailable", code = 500 })
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeInternalServiceTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-internal-token";
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test mediator only supports publish.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("This test mediator only supports publish.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test mediator only supports publish.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test mediator does not support streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test mediator does not support streams.");
        }
    }
}
