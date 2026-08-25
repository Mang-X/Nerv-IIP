using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record RegisterToolingAssetCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string ToolingType,
    IReadOnlyCollection<string> WorkCenterCodes,
    IReadOnlyCollection<string> SkuCodes,
    long? MaintenanceLifeCount,
    string? IdempotencyKey,
    ToolingOperationAuditContext AuditContext) : ICommand<MasterDataResourceResult>;

public sealed class RegisterToolingAssetCommandHandler(
    IToolingAssetRepository repository,
    MasterDataCodingService codingService,
    ApplicationDbContext dbContext,
    IToolingAuditOperationCoordinator operationCoordinator) : ICommandHandler<RegisterToolingAssetCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(RegisterToolingAssetCommand request, CancellationToken cancellationToken)
    {
        var context = request.AuditContext;
        var operationId = context.OperationId;
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
            !string.Equals(request.IdempotencyKey.Trim(), operationId, StringComparison.Ordinal))
        {
            throw new KnownException("工装注册请求体与审计标头中的幂等标识不一致。");
        }

        var fingerprint = ToolingAuditCommand.Fingerprint(
            ToolingAuditEntry.RegisterOperation,
            ToolingAuditCommand.NormalizeOptionalCode(request.Code),
            ToolingAssetStatus.Available,
            0L);
        return await operationCoordinator.ExecuteAsync(
            request.OrganizationId,
            request.EnvironmentId,
            operationId,
            request.Code,
            token => HandleCoreAsync(request, context, operationId, fingerprint, token),
            cancellationToken);
    }

    private async Task<MasterDataResourceResult> HandleCoreAsync(
        RegisterToolingAssetCommand request,
        ToolingOperationAuditContext context,
        string operationId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var replay = await ToolingAuditCommand.FindReplayAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            operationId,
            context.Actor,
            fingerprint,
            cancellationToken);
        if (replay is not null)
        {
            var replayedAsset = await repository.FindAsync(
                request.OrganizationId,
                request.EnvironmentId,
                replay.ToolingCode,
                cancellationToken)
                ?? throw new KnownException($"工装操作 '{operationId}' 指向的工装资产不存在。");
            return new MasterDataResourceResult("tooling-asset", replayedAsset.Code, replayedAsset.Name);
        }

        var codingFingerprint = MasterDataCodingService.Fingerprint(
            request.Name,
            request.ToolingType,
            request.WorkCenterCodes,
            request.SkuCodes,
            request.MaintenanceLifeCount);
        var allocation = await codingService.AllocateAsync(
            request.OrganizationId, request.EnvironmentId, "tooling-asset", request.Code, operationId,
            codingFingerprint,
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            throw new KnownException($"工装注册操作 '{operationId}' 缺少可归因的审计事实，禁止将历史操作归因给当前请求。");
        }
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, cancellationToken))
            throw new KnownException($"工装资产 '{allocation.Code}' 已存在。");

        var asset = ToolingAsset.Register(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Name,
            request.ToolingType, request.WorkCenterCodes, request.SkuCodes, request.MaintenanceLifeCount);
        await repository.AddAsync(asset, cancellationToken);
        dbContext.ToolingAuditEntries.Add(ToolingAuditEntry.Register(
            request.OrganizationId,
            request.EnvironmentId,
            asset.Id.ToString(),
            asset.Code,
            context.Actor,
            context.CorrelationId,
            context.CausationId,
            operationId,
            fingerprint,
            DateTimeOffset.UtcNow));
        return new MasterDataResourceResult("tooling-asset", asset.Code, asset.Name);
    }
}

public sealed record ChangeToolingStatusCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    ToolingAssetStatus Status,
    string Reason,
    ToolingOperationAuditContext AuditContext) : ICommand;

public sealed class ChangeToolingStatusCommandHandler(
    IToolingAssetRepository repository,
    ApplicationDbContext dbContext,
    IToolingAuditOperationCoordinator operationCoordinator) : ICommandHandler<ChangeToolingStatusCommand>
{
    public async Task Handle(ChangeToolingStatusCommand request, CancellationToken cancellationToken)
    {
        var context = request.AuditContext;
        var operationId = context.OperationId;
        var reason = ToolingAuditCommand.NormalizeReason(request.Reason);
        var fingerprint = ToolingAuditCommand.Fingerprint(
            ToolingAuditEntry.StatusOperation,
            ToolingAuditCommand.NormalizeRequiredCode(request.Code),
            request.Status,
            reason);
        await operationCoordinator.ExecuteAsync(
            request.OrganizationId,
            request.EnvironmentId,
            operationId,
            request.Code,
            async token =>
            {
                await HandleCoreAsync(request, context, operationId, reason, fingerprint, token);
                return true;
            },
            cancellationToken);
    }

    private async Task HandleCoreAsync(
        ChangeToolingStatusCommand request,
        ToolingOperationAuditContext context,
        string operationId,
        string reason,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (await ToolingAuditCommand.FindReplayAsync(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                operationId,
                context.Actor,
                fingerprint,
                cancellationToken) is not null)
        {
            return;
        }

        var asset = await repository.FindAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken)
            ?? throw new KnownException($"未找到工装资产 '{request.Code}'。");
        var before = asset.Status;
        asset.ChangeStatus(request.Status, reason);
        dbContext.ToolingAuditEntries.Add(ToolingAuditEntry.Status(
            request.OrganizationId,
            request.EnvironmentId,
            asset.Id.ToString(),
            asset.Code,
            context.Actor,
            context.CorrelationId,
            context.CausationId,
            operationId,
            fingerprint,
            before,
            asset.Status,
            reason,
            DateTimeOffset.UtcNow));
    }
}

public sealed record RecordToolingUsageCommand(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    long Count,
    ToolingOperationAuditContext AuditContext) : ICommand;
public sealed class RecordToolingUsageCommandHandler(
    IToolingAssetRepository repository,
    ApplicationDbContext dbContext,
    IToolingAuditOperationCoordinator operationCoordinator) : ICommandHandler<RecordToolingUsageCommand>
{
    public async Task Handle(RecordToolingUsageCommand request, CancellationToken cancellationToken)
    {
        var context = request.AuditContext;
        var operationId = context.OperationId;
        var fingerprint = ToolingAuditCommand.Fingerprint(
            ToolingAuditEntry.UsageOperation,
            ToolingAuditCommand.NormalizeRequiredCode(request.Code),
            request.Count);
        await operationCoordinator.ExecuteAsync(
            request.OrganizationId,
            request.EnvironmentId,
            operationId,
            request.Code,
            async token =>
            {
                await HandleCoreAsync(request, context, operationId, fingerprint, token);
                return true;
            },
            cancellationToken);
    }

    private async Task HandleCoreAsync(
        RecordToolingUsageCommand request,
        ToolingOperationAuditContext context,
        string operationId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (await ToolingAuditCommand.FindReplayAsync(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                operationId,
                context.Actor,
                fingerprint,
                cancellationToken) is not null)
        {
            return;
        }

        var asset = await repository.FindAsync(request.OrganizationId, request.EnvironmentId, request.Code, cancellationToken)
            ?? throw new KnownException($"未找到工装资产 '{request.Code}'。");
        var before = asset.UsageCount;
        asset.RecordUsage(request.Count);
        dbContext.ToolingAuditEntries.Add(ToolingAuditEntry.Usage(
            request.OrganizationId,
            request.EnvironmentId,
            asset.Id.ToString(),
            asset.Code,
            context.Actor,
            context.CorrelationId,
            context.CausationId,
            operationId,
            fingerprint,
            before,
            asset.UsageCount,
            request.Count,
            DateTimeOffset.UtcNow));
    }
}

internal static class ToolingAuditCommand
{
    public static string NormalizeReason(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? throw new KnownException("工装状态变更原因不能为空。")
            : reason.Trim();

    public static string NormalizeRequiredCode(string code) =>
        string.IsNullOrWhiteSpace(code)
            ? throw new KnownException("工装编码不能为空。")
            : code.Trim().ToUpperInvariant();

    public static string NormalizeOptionalCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "<allocated>" : code.Trim().ToUpperInvariant();

    public static string Fingerprint(params object?[] parts)
    {
        var canonical = MasterDataCodingService.Fingerprint(parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static async Task<ToolingAuditEntry?> FindReplayAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string operationId,
        string actor,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ToolingAuditEntries.AsNoTracking().SingleOrDefaultAsync(
            entry => entry.OrganizationId == organizationId &&
                entry.EnvironmentId == environmentId &&
                entry.OperationId == operationId,
            cancellationToken);
        if (existing is null)
        {
            return null;
        }
        if (!existing.Matches(actor.Trim(), fingerprint))
        {
            throw new KnownException($"工装操作 '{operationId}' 与此前持久化的请求内容冲突。");
        }

        return existing;
    }
}

public sealed record ChangeoverMatrixEntryDraft(string WorkCenterCode, string? FromSkuCode, string? FromProductCategoryCode, string ToSkuCode, int SetupMinutes, IReadOnlyCollection<string> RequiredToolingCodes, bool Active = true);
public sealed record ImportChangeoverMatrixCommand(string OrganizationId, string EnvironmentId, IReadOnlyCollection<ChangeoverMatrixEntryDraft> Entries) : ICommand<int>;

public sealed class ImportChangeoverMatrixCommandHandler(ApplicationDbContext dbContext, IChangeoverMatrixEntryRepository repository) : ICommandHandler<ImportChangeoverMatrixCommand, int>
{
    public async Task<int> Handle(ImportChangeoverMatrixCommand request, CancellationToken cancellationToken)
    {
        if (request.Entries.Count == 0) throw new KnownException("换型矩阵至少需要一条记录。");
        var toolingCodes = request.Entries.SelectMany(x => x.RequiredToolingCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var schedulableTooling = await dbContext.ToolingAssets
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && toolingCodes.Contains(x.Code))
            .Select(x => x.Code).ToArrayAsync(cancellationToken);
        var missing = toolingCodes.Except(schedulableTooling, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0) throw new KnownException($"以下工装资产不存在：{string.Join(", ", missing)}。");

        var keys = request.Entries.Select(Key).ToArray();
        if (keys.Distinct().Count() != keys.Length) throw new KnownException("导入内容包含重复的换型矩阵键。");
        var existing = await dbContext.ChangeoverMatrixEntries
            .Include(x => x.RequiredTooling)
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .ToArrayAsync(cancellationToken);

        foreach (var draft in request.Entries)
        {
            var key = Key(draft);
            var current = existing.SingleOrDefault(x => Key(x) == key);
            if (current is null)
            {
                var created = ChangeoverMatrixEntry.Create(request.OrganizationId, request.EnvironmentId, draft.WorkCenterCode,
                    draft.FromSkuCode, draft.FromProductCategoryCode, draft.ToSkuCode, draft.SetupMinutes, draft.RequiredToolingCodes);
                if (!draft.Active) created.Update(draft.SetupMinutes, draft.RequiredToolingCodes, false);
                await repository.AddAsync(created, cancellationToken);
            }
            else
                current.Update(draft.SetupMinutes, draft.RequiredToolingCodes, draft.Active);
        }
        return request.Entries.Count;
    }

    private sealed record ChangeoverKey(string WorkCenterCode, ChangeoverSourceType SourceType, string SourceCode, string ToSkuCode)
    {
        public static ChangeoverKey Create(string workCenter, string? fromSku, string? family, string toSku) => new(
            workCenter.Trim().ToUpperInvariant(), string.IsNullOrWhiteSpace(fromSku) ? ChangeoverSourceType.ProductCategory : ChangeoverSourceType.Sku,
            (fromSku ?? family ?? string.Empty).Trim().ToUpperInvariant(), toSku.Trim().ToUpperInvariant());
    }
    private static ChangeoverKey Key(ChangeoverMatrixEntryDraft x) => ChangeoverKey.Create(x.WorkCenterCode, x.FromSkuCode, x.FromProductCategoryCode, x.ToSkuCode);
    private static ChangeoverKey Key(ChangeoverMatrixEntry x) => new(x.WorkCenterCode.ToUpperInvariant(), x.SourceType, x.SourceCode.ToUpperInvariant(), x.ToSkuCode.ToUpperInvariant());
}
