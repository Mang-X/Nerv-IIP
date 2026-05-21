using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure.Records;

namespace Nerv.IIP.FileStorage.Infrastructure;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<StoredFileRecord> StoredFiles => Set<StoredFileRecord>();
    public DbSet<UploadSessionRecord> UploadSessions => Set<UploadSessionRecord>();
    public DbSet<DownloadGrantRecord> DownloadGrants => Set<DownloadGrantRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("filestorage");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
