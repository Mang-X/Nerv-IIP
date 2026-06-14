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
            throw new KnownException($"Product category '{allocation.Code}' already exists.");
        }

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
            ?? throw new KnownException($"Product category '{categoryCode}' was not found.");
    }
}

public sealed class ArchiveProductCategoryCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ArchiveProductCategoryCommand, ProductCategoryItem>
{
    public async Task<ProductCategoryItem> Handle(ArchiveProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await UpdateProductCategoryCommandHandler.FindAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.CategoryCode,
            cancellationToken);
        category.Disable(request.Reason);
        var categories = await ListProductCategoriesQueryHandler.LoadCategoriesAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        return ListProductCategoriesQueryHandler.ToItem(category, categories);
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
            throw new KnownException("Product category cannot reference itself as parent.");
        }

        var categories = await dbContext.ProductCategories
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => new { x.CategoryCode, x.ParentCode })
            .ToListAsync(cancellationToken);
        var byCode = categories.ToDictionary(x => x.CategoryCode, x => x.ParentCode, StringComparer.OrdinalIgnoreCase);
        if (!byCode.ContainsKey(normalizedParent))
        {
            throw new KnownException($"Parent product category '{normalizedParent}' was not found.");
        }

        var current = normalizedParent;
        while (byCode.TryGetValue(current, out var nextParent) && !string.IsNullOrWhiteSpace(nextParent))
        {
            if (string.Equals(nextParent, categoryCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new KnownException("Product category parent cannot be a descendant.");
            }

            current = nextParent;
        }
    }
}
