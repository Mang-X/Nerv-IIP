using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;

public sealed record CreateStockCountTaskCommand(
    string OrganizationId,
    string EnvironmentId,
    string CountTaskCode,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    string? IdempotencyKey = null) : ICommand<CreateStockCountTaskResult>;

public sealed record CreateStockCountTaskResult(StockCountTaskId CountTaskId, long ExpectedLedgerVersion);

public sealed class CreateStockCountTaskCommandLock : ICommandLock<CreateStockCountTaskCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(CreateStockCountTaskCommand command, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var lockKey = string.Join(
            ':',
            "business-inventory",
            "stock-count-task",
            Normalize(command.OrganizationId),
            Normalize(command.EnvironmentId),
            Normalize(CreateStockCountTaskIdempotency.Resolve(command)));
        return Task.FromResult(new CommandLockSettings(lockKey, 30));
    }

    private static string Normalize(string value)
    {
        return Uri.EscapeDataString(value.Trim());
    }
}

public sealed class CreateStockCountTaskCommandValidator : AbstractValidator<CreateStockCountTaskCommand>
{
    public CreateStockCountTaskCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.CountTaskCode).RequiredInventoryCode(100);
        RuleFor(x => x.SkuCode).RequiredInventoryCode(100);
        RuleFor(x => x.UomCode).RequiredInventoryCode(50);
        RuleFor(x => x.SiteCode).RequiredInventoryCode(100);
        RuleFor(x => x.LocationCode).RequiredInventoryCode(100);
        RuleFor(x => x.LotNo).OptionalInventoryCode(100);
        RuleFor(x => x.SerialNo).OptionalInventoryCode(100);
        RuleFor(x => x.QualityStatus).RequiredInventoryCode(50);
        RuleFor(x => x.OwnerType).RequiredInventoryCode(50);
        RuleFor(x => x.OwnerId).OptionalInventoryCode(100);
        RuleFor(x => x.IdempotencyKey).OptionalInventoryCode(InventoryValidationRules.IdempotencyKeyMaxLength);
    }
}

public sealed class CreateStockCountTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateStockCountTaskCommand, CreateStockCountTaskResult>
{
    public async Task<CreateStockCountTaskResult> Handle(CreateStockCountTaskCommand request, CancellationToken cancellationToken)
    {
        var qualityStatus = StockQualityStatus.Normalize(request.QualityStatus);
        var ownerType = StockOwnerType.Normalize(request.OwnerType);
        var idempotencyKey = CreateStockCountTaskIdempotency.Resolve(request);
        var existing = await dbContext.StockCountTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.HasSameCreationScope(
                    request.CountTaskCode,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId))
            {
                throw new KnownException("Stock count task idempotency key conflicts with an existing count scope.");
            }

            return new CreateStockCountTaskResult(existing.Id, existing.ExpectedLedgerVersion);
        }

        var existingCountCode = await dbContext.StockCountTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.CountTaskCode == request.CountTaskCode,
            cancellationToken);
        if (existingCountCode is not null)
        {
            throw new KnownException("Stock count task code conflicts with an existing idempotency key.");
        }

        var ledger = await dbContext.StockLedgers.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SkuCode == request.SkuCode
                && x.UomCode == request.UomCode
                && x.SiteCode == request.SiteCode
                && x.LocationCode == request.LocationCode
                && x.LotNo == request.LotNo
                && x.SerialNo == request.SerialNo
                && x.QualityStatus == qualityStatus
                && x.OwnerType == ownerType
                && x.OwnerId == request.OwnerId,
            cancellationToken)
            ?? throw new KnownException("Stock ledger does not exist for the requested count scope.");

        var task = StockCountTask.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.CountTaskCode,
            idempotencyKey,
            ledger.OrganizationId,
            ledger.EnvironmentId,
            ledger.SkuCode,
            ledger.UomCode,
            ledger.SiteCode,
            ledger.LocationCode,
            ledger.LotNo,
            ledger.SerialNo,
            ledger.QualityStatus,
            ledger.OwnerType,
            ledger.OwnerId,
            ledger.LedgerVersion);
        ledger.FreezeForCount(task.CountTaskCode);
        dbContext.StockCountTasks.Add(task);
        return new CreateStockCountTaskResult(task.Id, task.ExpectedLedgerVersion);
    }
}

public sealed class CreateStockCountTaskUniqueConflictBehavior<TRequest, TResponse>(ApplicationDbContext dbContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not CreateStockCountTaskCommand)
        {
            return await next(cancellationToken);
        }

        try
        {
            return await next(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsStockCountTaskUniqueConflict(ex, dbContext))
        {
            dbContext.ChangeTracker.Clear();
            return await next(cancellationToken);
        }
    }

    private static bool IsStockCountTaskUniqueConflict(DbUpdateException exception, ApplicationDbContext context)
    {
        return exception.Entries.Any(entry => entry.Entity is StockCountTask) &&
            EnumerateExceptions(exception).Any(inner =>
                IsPostgreSqlUniqueConflict(inner) ||
                IsSqliteUniqueConflict(context, inner) ||
                IsSqlServerUniqueConflict(context, inner) ||
                IsMySqlUniqueConflict(context, inner));
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static bool IsPostgreSqlUniqueConflict(Exception exception)
    {
        if (!string.Equals(exception.GetType().FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
        {
            return false;
        }

        return exception.GetType().GetProperty("SqlState")?.GetValue(exception) as string == "23505";
    }

    private static bool IsSqliteUniqueConflict(ApplicationDbContext context, Exception exception)
    {
        var providerName = context.Database.ProviderName ?? string.Empty;
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) &&
            !typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var errorCode = GetIntProperty(exception, "SqliteErrorCode");
        var extendedErrorCode = GetIntProperty(exception, "SqliteExtendedErrorCode");
        return errorCode == 19 || extendedErrorCode is 1555 or 2067;
    }

    private static bool IsSqlServerUniqueConflict(ApplicationDbContext context, Exception exception)
    {
        var providerName = context.Database.ProviderName ?? string.Empty;
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) &&
            !typeName.Contains("SqlException", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return GetIntProperty(exception, "Number") is 2601 or 2627;
    }

    private static bool IsMySqlUniqueConflict(ApplicationDbContext context, Exception exception)
    {
        var providerName = context.Database.ProviderName ?? string.Empty;
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase) &&
            !typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return GetIntProperty(exception, "Number") == 1062;
    }

    private static int? GetIntProperty(Exception exception, string propertyName)
    {
        var value = exception.GetType().GetProperty(propertyName)?.GetValue(exception);
        return value switch
        {
            int intValue => intValue,
            uint uintValue => unchecked((int)uintValue),
            _ => null,
        };
    }
}

internal static class CreateStockCountTaskIdempotency
{
    public static string Resolve(CreateStockCountTaskCommand request)
    {
        return string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"count-code:{request.CountTaskCode.Trim()}"
            : request.IdempotencyKey.Trim();
    }
}
