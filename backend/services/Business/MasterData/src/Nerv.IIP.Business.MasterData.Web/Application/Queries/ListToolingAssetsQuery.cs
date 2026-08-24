using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record ToolingAssetListItem(
    string Code,
    string Name,
    string ToolingType,
    [property: JsonConverter(typeof(ToolingAssetStatusJsonConverter))]
    ToolingAssetStatus Status,
    long? MaintenanceLifeCount,
    long UsageCount,
    bool IsSchedulable,
    IReadOnlyCollection<string> WorkCenterCodes,
    IReadOnlyCollection<string> SkuCodes);

public sealed record ToolingAssetListResponse(IReadOnlyCollection<ToolingAssetListItem> Items, int Total);

public sealed class ToolingAssetStatusJsonConverter()
    : JsonStringEnumConverter<ToolingAssetStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public sealed record ListToolingAssetsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Keyword = null,
    ToolingAssetStatus? Status = null,
    int Skip = 0,
    int Take = 100) : IQuery<ToolingAssetListResponse>;

public sealed class ListToolingAssetsQueryValidator : AbstractValidator<ListToolingAssetsQuery>
{
    public ListToolingAssetsQueryValidator()
    {
        RuleFor(request => request.OrganizationId)
            .NotEmpty().WithMessage("组织标识不能为空。")
            .MaximumLength(64).WithMessage("组织标识不能超过 64 个字符。");
        RuleFor(request => request.EnvironmentId)
            .NotEmpty().WithMessage("环境标识不能为空。")
            .MaximumLength(64).WithMessage("环境标识不能超过 64 个字符。");
        RuleFor(request => request.Keyword)
            .MaximumLength(200).WithMessage("关键字不能超过 200 个字符。");
        RuleFor(request => request.Status)
            .IsInEnum().WithMessage("工装状态无效。")
            .When(request => request.Status.HasValue);
        RuleFor(request => request.Skip)
            .GreaterThanOrEqualTo(0).WithMessage("skip 不能小于 0。");
        RuleFor(request => request.Take)
            .InclusiveBetween(1, 500).WithMessage("take 必须在 1 至 500 之间。");
    }
}

public sealed class ListToolingAssetsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListToolingAssetsQuery, ToolingAssetListResponse>
{
    public async Task<ToolingAssetListResponse> Handle(
        ListToolingAssetsQuery request,
        CancellationToken cancellationToken)
    {
        var keyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim().ToLowerInvariant();
        var query = dbContext.ToolingAssets
            .AsNoTracking()
            .Where(asset =>
                asset.OrganizationId == request.OrganizationId &&
                asset.EnvironmentId == request.EnvironmentId)
            .Where(asset => !request.Status.HasValue || asset.Status == request.Status.Value)
            .Where(asset => keyword == null ||
                asset.Code.ToLower().Contains(keyword) ||
                asset.Name.ToLower().Contains(keyword) ||
                asset.ToolingType.ToLower().Contains(keyword));

        var total = await query.CountAsync(cancellationToken);
        var assets = await query
            .Include(asset => asset.Applicability)
            .OrderBy(asset => asset.Code)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToArrayAsync(cancellationToken);

        var items = assets.Select(asset => new ToolingAssetListItem(
            asset.Code,
            asset.Name,
            asset.ToolingType,
            asset.Status,
            asset.MaintenanceLifeCount,
            asset.UsageCount,
            asset.IsSchedulable,
            asset.Applicability
                .Select(applicability => applicability.WorkCenterCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            asset.Applicability
                .Select(applicability => applicability.SkuCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray()))
            .ToArray();

        return new ToolingAssetListResponse(items, total);
    }
}
