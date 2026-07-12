using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;

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
    string? IdempotencyKey) : ICommand<MasterDataResourceResult>;

public sealed class RegisterToolingAssetCommandHandler(
    IToolingAssetRepository repository,
    MasterDataCodingService codingService) : ICommandHandler<RegisterToolingAssetCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(RegisterToolingAssetCommand request, CancellationToken cancellationToken)
    {
        var allocation = await codingService.AllocateAsync(
            request.OrganizationId, request.EnvironmentId, "tooling-asset", request.Code, request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.ToolingType, request.WorkCenterCodes, request.SkuCodes, request.MaintenanceLifeCount),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
            return new MasterDataResourceResult("tooling-asset", allocation.Code, request.Name);
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, cancellationToken))
            throw new KnownException($"Tooling asset '{allocation.Code}' already exists.");

        var asset = ToolingAsset.Register(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Name,
            request.ToolingType, request.WorkCenterCodes, request.SkuCodes, request.MaintenanceLifeCount);
        await repository.AddAsync(asset, cancellationToken);
        return new MasterDataResourceResult("tooling-asset", asset.Code, asset.Name);
    }
}

public sealed record ChangeToolingStatusCommand(string OrganizationId, string EnvironmentId, string Code, ToolingAssetStatus Status, string Reason) : ICommand;

public sealed class ChangeToolingStatusCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<ChangeToolingStatusCommand>
{
    public async Task Handle(ChangeToolingStatusCommand request, CancellationToken cancellationToken)
    {
        var asset = await dbContext.ToolingAssets.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
            ?? throw new KnownException($"Tooling asset '{request.Code}' was not found.");
        asset.ChangeStatus(request.Status, request.Reason);
    }
}

public sealed record RecordToolingUsageCommand(string OrganizationId, string EnvironmentId, string Code, long Count) : ICommand;
public sealed class RecordToolingUsageCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<RecordToolingUsageCommand>
{
    public async Task Handle(RecordToolingUsageCommand request, CancellationToken cancellationToken)
    {
        var asset = await dbContext.ToolingAssets.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Code == request.Code, cancellationToken)
            ?? throw new KnownException($"Tooling asset '{request.Code}' was not found.");
        asset.RecordUsage(request.Count);
    }
}

public sealed record ChangeoverMatrixEntryDraft(string WorkCenterCode, string? FromSkuCode, string? FromProductFamilyCode, string ToSkuCode, int SetupMinutes, IReadOnlyCollection<string> RequiredToolingCodes, bool Active = true);
public sealed record ImportChangeoverMatrixCommand(string OrganizationId, string EnvironmentId, IReadOnlyCollection<ChangeoverMatrixEntryDraft> Entries) : ICommand<int>;

public sealed class ImportChangeoverMatrixCommandHandler(ApplicationDbContext dbContext, IChangeoverMatrixEntryRepository repository) : ICommandHandler<ImportChangeoverMatrixCommand, int>
{
    public async Task<int> Handle(ImportChangeoverMatrixCommand request, CancellationToken cancellationToken)
    {
        if (request.Entries.Count == 0) throw new KnownException("At least one changeover matrix entry is required.");
        var toolingCodes = request.Entries.SelectMany(x => x.RequiredToolingCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var schedulableTooling = await dbContext.ToolingAssets
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && toolingCodes.Contains(x.Code))
            .Select(x => x.Code).ToArrayAsync(cancellationToken);
        var missing = toolingCodes.Except(schedulableTooling, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0) throw new KnownException($"Unknown tooling assets: {string.Join(", ", missing)}.");

        var keys = request.Entries.Select(Key).ToArray();
        if (keys.Distinct().Count() != keys.Length) throw new KnownException("The import contains duplicate changeover matrix keys.");
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
                    draft.FromSkuCode, draft.FromProductFamilyCode, draft.ToSkuCode, draft.SetupMinutes, draft.RequiredToolingCodes);
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
            workCenter.Trim().ToUpperInvariant(), string.IsNullOrWhiteSpace(fromSku) ? ChangeoverSourceType.ProductFamily : ChangeoverSourceType.Sku,
            (fromSku ?? family ?? string.Empty).Trim().ToUpperInvariant(), toSku.Trim().ToUpperInvariant());
    }
    private static ChangeoverKey Key(ChangeoverMatrixEntryDraft x) => ChangeoverKey.Create(x.WorkCenterCode, x.FromSkuCode, x.FromProductFamilyCode, x.ToSkuCode);
    private static ChangeoverKey Key(ChangeoverMatrixEntry x) => new(x.WorkCenterCode.ToUpperInvariant(), x.SourceType, x.SourceCode.ToUpperInvariant(), x.ToSkuCode.ToUpperInvariant());
}
