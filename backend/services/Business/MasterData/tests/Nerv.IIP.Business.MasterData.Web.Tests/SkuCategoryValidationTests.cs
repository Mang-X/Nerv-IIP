using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

/// <summary>
/// SKU 的「产品分类」值域契约（#1596，口径裁决 A）。
///
/// 背景：界面分类下拉取的是**产品分类目录实体**（`PCAT-*` 层级树），后端却按 reference-data
/// 的 `product-category` CodeSet 校验（种子是 `electronic` 之类），两个值空间完全不相交——
/// 下拉里选什么都被拒，新建物料表单根本提交不出去。
///
/// 裁决：分类以**实体**为权威值域；`product-category` CodeSet 降级为 legacy，按
/// `master-data-dictionary-rules.md` §1「独立目录兼容」保留兼容读取，不再是新写入的校验源。
/// </summary>
public sealed class SkuCategoryValidationTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";

    [Fact]
    public async Task Category_referencing_an_active_product_category_entity_is_accepted()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductCategories.Add(ProductCategory.Create(Org, Env, "PCAT-SHOCK-FR", "前减振器", "PCAT-SHOCK", null));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await SkuCategoryValidator.ValidateAsync(dbContext, null, Org, Env, "PCAT-SHOCK-FR", CancellationToken.None);
    }

    [Fact]
    public async Task Category_referencing_a_disabled_product_category_entity_is_rejected()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = ProductCategory.Create(Org, Env, "PCAT-SHOCK-RR", "后减振器", "PCAT-SHOCK", null);
        category.Disable("车型停产");
        dbContext.ProductCategories.Add(category);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<KnownException>(() =>
            SkuCategoryValidator.ValidateAsync(dbContext, null, Org, Env, "PCAT-SHOCK-RR", CancellationToken.None));
        Assert.Contains("PCAT-SHOCK-RR", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Category_referencing_nothing_at_all_is_rejected()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var error = await Assert.ThrowsAsync<KnownException>(() =>
            SkuCategoryValidator.ValidateAsync(dbContext, null, Org, Env, "PCAT-NOT-THERE", CancellationToken.None));
        Assert.Contains("PCAT-NOT-THERE", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 兼容轴：文档要求「完全切换前不得破坏 SKU `category` 对 CodeSet 的兼容读取」，
    /// 所以存量 legacy 码仍须被接受——否则编辑一条老物料就会被自己的历史分类挡住。
    /// </summary>
    [Fact]
    public async Task Legacy_reference_data_code_stays_accepted_during_the_transition()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create(Org, Env, "product-category", "electronic", "电子料"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await SkuCategoryValidator.ValidateAsync(dbContext, null, Org, Env, "electronic", CancellationToken.None);
    }

    [Fact]
    public async Task Disabled_legacy_reference_data_code_is_rejected()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var legacy = ReferenceDataCode.Create(Org, Env, "product-category", "chemical", "化学品");
        legacy.Disable("并入危化品");
        dbContext.ReferenceDataCodes.Add(legacy);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<KnownException>(() =>
            SkuCategoryValidator.ValidateAsync(dbContext, null, Org, Env, "chemical", CancellationToken.None));
    }

    [Fact]
    public async Task Category_is_scoped_to_the_requesting_organization_and_environment()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductCategories.Add(ProductCategory.Create("org-other", Env, "PCAT-SHOCK", "减振器总成", null, null));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<KnownException>(() =>
            SkuCategoryValidator.ValidateAsync(dbContext, null, Org, Env, "PCAT-SHOCK", CancellationToken.None));
    }

    /// <summary>更新命令未提交该字段时（null）不应触发校验，否则改个名字都要带上分类。</summary>
    [Fact]
    public async Task Omitted_category_on_update_is_not_validated()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await SkuCategoryValidator.ValidateAsync(dbContext, null, Org, Env, null, CancellationToken.None);
    }

    [Fact]
    public async Task Blank_category_is_rejected_rather_than_silently_accepted()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.ThrowsAsync<KnownException>(() =>
            SkuCategoryValidator.ValidateAsync(dbContext, null, Org, Env, "   ", CancellationToken.None));
    }

    /// <summary>
    /// 反向契约：`product-category` 不得再出现在 SKU 的 CodeSet 校验清单里。
    /// 留着它就等于双重值域——实体过了、字典又拦一道，本 issue 的 400 会原样复发。
    /// </summary>
    [Fact]
    public void Product_category_is_no_longer_validated_through_the_reference_data_code_sets()
    {
        var createSets = MasterDataDictionaryRules
            .GetCreateSkuReferences("component", "not-tracked", "not-tracked", "not-managed", "ambient", "code128", [])
            .Select(x => x.CodeSet)
            .ToArray();
        var updateSets = MasterDataDictionaryRules
            .GetUpdateSkuReferences("component", "not-tracked", "not-tracked", "not-managed", "ambient", "code128")
            .Select(x => x.CodeSet)
            .ToArray();

        Assert.DoesNotContain("product-category", createSets, StringComparer.Ordinal);
        Assert.DoesNotContain("product-category", updateSets, StringComparer.Ordinal);
        // 其余受控字典不受影响。
        Assert.Contains("material-type", createSets, StringComparer.Ordinal);
        Assert.Contains("material-type", updateSets, StringComparer.Ordinal);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"sku-category-validation-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
