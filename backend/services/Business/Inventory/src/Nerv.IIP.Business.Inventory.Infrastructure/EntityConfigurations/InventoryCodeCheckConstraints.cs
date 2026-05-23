namespace Nerv.IIP.Business.Inventory.Infrastructure.EntityConfigurations;

internal static class InventoryCodeCheckConstraints
{
    private const string CodePattern = "^[A-Za-z0-9_.:-]+$";

    public static void Add<TEntity>(TableBuilder<TEntity> tableBuilder, string name, string columnName)
        where TEntity : class
    {
        tableBuilder.HasCheckConstraint(name, $"{columnName} ~ '{CodePattern}'");
    }
}
