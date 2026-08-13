namespace Nerv.IIP.Business.Inventory.Web.Tests;

// NERV-688 #1561：InventoryDirectory 的 external 用例并入 inventory-postgres-profile 成员后，
// 该成员的两条冻结身份分属两个类。它们共用 runner 注入的同一个成员数据库，各自又都以
// DROP SCHEMA inventory CASCADE 开场，因此必须串行——否则一个类正在迁移时另一个把 schema 删掉，
// 实测表现为 42710 constraint already exists / 3F000 schema does not exist。
[CollectionDefinition(InventoryPostgresLaneCollection.Name, DisableParallelization = true)]
public sealed class InventoryPostgresLaneCollection
{
    public const string Name = "InventoryPostgresLane";
}
