using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nerv.IIP.FileStorage.Infrastructure;

public sealed class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=nerv_iip_filestorage_design;Username=nerv;Password=nerv",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "filestorage"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
