using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Nerv.IIP.Testing.EntityFramework;

/// <summary>
/// SQLite provider 无法翻译 DateTimeOffset 的排序/聚合/比较（仓库已知坑：EF 测试 provider 翻译差异），
/// 测试专用 ModelCustomizer 把所有 DateTimeOffset 列统一转成 long（值均为 UTC，ToBinary 排序与时间序一致）。
/// 用 <c>ReplaceService&lt;IModelCustomizer, SqliteDateTimeOffsetModelCustomizer&gt;()</c> 装配，
/// 不能子类化 ApplicationDbContext：netcorepal source generator 会对派生类生成不兼容的 partial 覆写。
/// </summary>
public sealed class SqliteDateTimeOffsetModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    private static readonly DateTimeOffsetToBinaryConverter Converter = new();

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(Converter);
            }
        }
    }
}
