namespace Nerv.IIP.Business.Mes.Web.Tests;

internal static class MesMaterialRequirementEfSourceFixtures
{
    public static TheoryData<string, string> EquivalentEfReadBypasses => new()
    {
        { "DbSet property through receiver alias", "var context = dbContext; var rows = context.MaterialRequirements;" },
        { "generic Set", "var rows = dbContext.Set<MaterialRequirement>();" },
        { "generic Set through receiver and entity aliases", "DbContext context = dbContext; var rows = context.Set<Requirement>();" },
        { "dynamic receiver with a resolved entity alias", "dynamic context = dbContext; var rows = context.Set<Requirement>();" },
        { "shared type Set", "var rows = dbContext.Set<MaterialRequirement>(\"material-requirements\");" },
        { "Set Type reflection", "var rows = typeof(DbContext).GetMethod(nameof(DbContext.Set))!.MakeGenericMethod(typeof(MaterialRequirement));" },
        { "typed raw query", "var rows = dbContext.Database.SqlQuery<MaterialRequirement>($\"SELECT * FROM mes.material_requirements\");" },
        { "typed raw string query", "var rows = dbContext.Database.SqlQueryRaw<MaterialRequirement>(\"SELECT * FROM mes.material_requirements\");" },
        { "FromSql rooted at generic Set", "var rows = dbContext.Set<MaterialRequirement>().FromSqlRaw(\"SELECT * FROM mes.material_requirements\");" },
        { "generic Set method group", "Func<DbSet<MaterialRequirement>> read = dbContext.Set<MaterialRequirement>; var rows = read();" },
        { "ChangeTracker entries", "var rows = dbContext.ChangeTracker.Entries<MaterialRequirement>();" },
        { "generic Find", "var row = dbContext.Find<MaterialRequirement>(new object());" },
        { "generic FindAsync cancellation overload", "var row = dbContext.FindAsync<MaterialRequirement>([new object()], CancellationToken.None);" },
        { "Type Find", "var row = dbContext.Find(typeof(MaterialRequirement), new object());" },
        { "Type FindAsync cancellation overload", "var row = dbContext.FindAsync(typeof(MaterialRequirement), [new object()], CancellationToken.None);" },
        { "unknown Type Find fails closed", "Type entityType = DateTime.UtcNow.Ticks > 0 ? typeof(MaterialRequirement) : typeof(string); var row = dbContext.Find(entityType, new object());" },
        { "reassigned Type Find fails closed", "Type entityType = typeof(OtherEntity); entityType = DateTime.UtcNow.Ticks > 0 ? typeof(MaterialRequirement) : typeof(string); var row = dbContext.Find(entityType, new object());" },
        { "non-generic ChangeTracker entries", "var rows = dbContext.ChangeTracker.Entries().Where(entry => entry.Entity is MaterialRequirement);" },
        { "generic Find method reference", "Func<object?[], MaterialRequirement?> find = dbContext.Find<MaterialRequirement>; var row = find([new object()]);" },
        { "non-generic Entries method reference", "Func<IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>> entries = dbContext.ChangeTracker.Entries; var rows = entries();" },
        { "FromExpression invocation", "var rows = dbContext.FromExpression<MaterialRequirement>(() => Array.Empty<MaterialRequirement>().AsQueryable());" },
        { "FromExpression method reference", "Func<Expression<Func<IQueryable<MaterialRequirement>>>, IQueryable<MaterialRequirement>> query = dbContext.FromExpression<MaterialRequirement>; var rows = query(() => Array.Empty<MaterialRequirement>().AsQueryable());" },
        { "dynamic non-generic Find direct Type", "dynamic context = dbContext; var row = context.Find(typeof(MaterialRequirement), new object());" },
        { "dynamic non-generic Find direct receiver", "var row = ((dynamic)dbContext).Find(typeof(MaterialRequirement), new object());" },
        { "dynamic non-generic Find local Type", "Type entityType = typeof(MaterialRequirement); dynamic context = dbContext; var row = context.Find(entityType, new object());" },
        { "dynamic non-generic Find unknown Type fails closed", "Type entityType = DateTime.UtcNow.Ticks > 0 ? typeof(MaterialRequirement) : typeof(OtherEntity); dynamic context = dbContext; var row = context.Find(entityType, new object());" },
        { "dynamic non-generic ChangeTracker Entries", "dynamic tracker = dbContext.ChangeTracker; var rows = tracker.Entries();" },
        { "dynamic non-generic ChangeTracker Entries direct receiver", "var rows = ((dynamic)dbContext.ChangeTracker).Entries();" },
    };

    public static TheoryData<string, string> KnownNonTargetEfReadShapes => new()
    {
        { "Set OtherEntity", "var rows = dbContext.Set<OtherEntity>();" },
        { "Find OtherEntity", "var row = dbContext.Find<OtherEntity>(new object());" },
        { "FindAsync OtherEntity", "var row = dbContext.FindAsync<OtherEntity>([new object()], CancellationToken.None);" },
        { "Entries OtherEntity", "var rows = dbContext.ChangeTracker.Entries<OtherEntity>();" },
        { "SqlQuery OtherEntity", "var rows = dbContext.Database.SqlQuery<OtherEntity>($\"SELECT 1\");" },
        { "SqlQueryRaw OtherEntity", "var rows = dbContext.Database.SqlQueryRaw<OtherEntity>(\"SELECT 1\");" },
        { "FromSql OtherEntity", "var rows = dbContext.Set<OtherEntity>().FromSql($\"SELECT 1\");" },
        { "FromSqlInterpolated OtherEntity", "var rows = dbContext.Set<OtherEntity>().FromSqlInterpolated($\"SELECT 1\");" },
        { "FromSqlRaw OtherEntity", "var rows = dbContext.Set<OtherEntity>().FromSqlRaw(\"SELECT 1\");" },
        { "FromExpression OtherEntity", "var rows = dbContext.FromExpression<OtherEntity>(() => Array.Empty<OtherEntity>().AsQueryable());" },
        { "runtime Type OtherEntity", "var row = dbContext.Find(typeof(OtherEntity), new object());" },
        { "runtime local Type OtherEntity", "Type entityType = typeof(OtherEntity); var row = dbContext.Find(entityType, new object());" },
        { "dynamic runtime Type OtherEntity", "dynamic context = dbContext; var row = context.Find(typeof(OtherEntity), new object());" },
        { "unrelated dynamic Entries", "dynamic state = new OtherDynamicState(); var rows = state.Entries();" },
    };
}
