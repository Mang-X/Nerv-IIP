using System.Text.Json;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ScopeContextAuditAggregate;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

internal static class MasterDataScopeContextAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Add(
        ApplicationDbContext dbContext,
        MasterDataIntegrationEventContext context,
        string organizationId,
        string environmentId,
        string operationKind,
        string resourceType,
        string resourceId,
        string resourceCode,
        string resourceIdentity,
        object? before,
        object? after,
        string reason)
    {
        dbContext.ScopeContextAuditEntries.Add(new MasterDataScopeContextAuditEntry(
            organizationId,
            environmentId,
            operationKind,
            resourceType,
            resourceId,
            resourceCode,
            resourceIdentity,
            context.Actor,
            context.CorrelationId,
            context.CausationId,
            context.IdempotencyKey ?? context.CorrelationId,
            JsonSerializer.Serialize(before, JsonOptions),
            JsonSerializer.Serialize(after, JsonOptions),
            reason,
            DateTimeOffset.UtcNow));
    }

    public static void AddCreated(
        ApplicationDbContext dbContext,
        MasterDataIntegrationEventContext? context,
        string organizationId,
        string environmentId,
        string resourceType,
        string resourceId,
        string resourceCode,
        object after)
    {
        Add(
            dbContext,
            context ?? throw new KnownException("An authenticated audit context is required for scope candidate creation."),
            organizationId,
            environmentId,
            $"{resourceType}-created",
            resourceType,
            resourceId,
            resourceCode,
            resourceCode,
            before: null,
            after,
            "scope-candidate-created");
    }
}
