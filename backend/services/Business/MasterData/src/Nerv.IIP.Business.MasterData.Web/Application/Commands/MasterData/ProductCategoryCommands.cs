using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record CreateProductCategoryCommand(
    string OrganizationId,
    string EnvironmentId,
    string? CategoryCode,
    string CategoryName,
    string? ParentCode,
    string? Description,
    string? IdempotencyKey = null) : ICommand<MasterDataResourceResult>;

public sealed record UpdateProductCategoryCommand(
    string OrganizationId,
    string EnvironmentId,
    string CategoryCode,
    string CategoryName,
    string? ParentCode,
    string? Description) : ICommand<ProductCategoryItem>;

public sealed record ArchiveProductCategoryCommand(
    string OrganizationId,
    string EnvironmentId,
    string CategoryCode,
    string Reason) : ICommand<ProductCategoryItem>;

public sealed class CreateProductCategoryCommandHandler(
    ApplicationDbContext dbContext,
    IProductCategoryRepository repository,
    MasterDataCodingService? codingService = null)
    : ICommandHandler<CreateProductCategoryCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "product-category",
            request.OrganizationId,
            request.EnvironmentId,
            request.CategoryCode,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.CategoryName, request.ParentCode, request.Description),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("product-category", allocation.Code, request.CategoryName);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, cancellationToken))
        {
            throw new KnownException($"产品分类 '{allocation.Code}' 已存在。");
        }

        await ProductCategoryTreeValidator.EnsureParentDoesNotCreateCycleAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.ParentCode,
            cancellationToken);

        var category = ProductCategory.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.CategoryName,
            request.ParentCode,
            request.Description);
        await repository.AddAsync(category, cancellationToken);
        return new MasterDataResourceResult("product-category", category.CategoryCode, category.CategoryName);
    }
}

public sealed class UpdateProductCategoryCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<UpdateProductCategoryCommand, ProductCategoryItem>
{
    public async Task<ProductCategoryItem> Handle(UpdateProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await FindAsync(dbContext, request.OrganizationId, request.EnvironmentId, request.CategoryCode, cancellationToken);
        await ProductCategoryTreeValidator.EnsureParentDoesNotCreateCycleAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.CategoryCode,
            request.ParentCode,
            cancellationToken);

        category.Update(request.CategoryName, request.ParentCode, request.Description);
        var categories = await ListProductCategoriesQueryHandler.LoadCategoriesAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        return ListProductCategoriesQueryHandler.ToItem(category, categories);
    }

    internal static async Task<ProductCategory> FindAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string categoryCode,
        CancellationToken cancellationToken)
    {
        return await dbContext.ProductCategories.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.CategoryCode == categoryCode,
            cancellationToken)
            ?? throw new KnownException($"未找到产品分类 '{categoryCode}'。");
    }
}

public sealed class ArchiveProductCategoryCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ArchiveProductCategoryCommand, ProductCategoryItem>
{
    public async Task<ProductCategoryItem> Handle(ArchiveProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var reason = MasterDataArchiveReason.NormalizeRequired(request.Reason);
        var category = await UpdateProductCategoryCommandHandler.FindAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.CategoryCode,
            cancellationToken);
        await EnsureCategoryIsNotReferencedAsync(request, cancellationToken);
        category.Disable(reason);
        var categories = await ListProductCategoriesQueryHandler.LoadCategoriesAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        return ListProductCategoriesQueryHandler.ToItem(category, categories);
    }

    private async Task EnsureCategoryIsNotReferencedAsync(ArchiveProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var hasActiveChild = await dbContext.ProductCategories.AnyAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            !x.Disabled &&
            x.ParentCode == request.CategoryCode,
            cancellationToken);
        if (hasActiveChild)
        {
            throw new KnownException($"产品分类 '{request.CategoryCode}' 仍有启用的子分类，不能归档。请先处理相关子分类。");
        }

        var referencedBySku = await dbContext.Skus.AnyAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            !x.Disabled &&
            x.Category == request.CategoryCode,
            cancellationToken);
        if (referencedBySku)
        {
            throw new KnownException($"产品分类 '{request.CategoryCode}' 仍被启用的 SKU 引用，不能归档。请先调整相关 SKU 的产品分类。");
        }
    }
}

internal static class MasterDataArchiveReason
{
    internal const int MaximumLength = 500;

    public static string NormalizeRequired(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new KnownException("归档原因不能为空。");
        }

        var normalized = reason.Trim();
        if (normalized.Length > MaximumLength)
        {
            throw new KnownException($"归档原因不能超过 {MaximumLength} 个字符。");
        }

        return normalized;
    }
}

internal static class ProductCategoryTreeValidator
{
    public static async Task EnsureParentDoesNotCreateCycleAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string categoryCode,
        string? parentCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parentCode))
        {
            return;
        }

        var normalizedParent = parentCode.Trim();
        if (string.Equals(categoryCode, normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException("产品分类不能将自身设置为父分类。");
        }

        var categories = await dbContext.ProductCategories
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new { x.CategoryCode, x.ParentCode })
            .ToListAsync(cancellationToken);
        var byCode = categories.ToDictionary(x => x.CategoryCode, x => x.ParentCode, StringComparer.OrdinalIgnoreCase);
        if (!byCode.ContainsKey(normalizedParent))
        {
            throw new KnownException($"未找到父产品分类 '{normalizedParent}'。");
        }

        var current = normalizedParent;
        while (byCode.TryGetValue(current, out var nextParent) && !string.IsNullOrWhiteSpace(nextParent))
        {
            if (string.Equals(nextParent, categoryCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new KnownException("产品分类不能将自己的后代分类设置为父分类。");
            }

            current = nextParent;
        }
    }
}
