using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

namespace Nerv.IIP.FileStorage.Web.Tests;

internal static class FileStorageServiceTestFactory
{
    public static PostgreSqlFileStorageService Create(
        ApplicationDbContext dbContext,
        IFileStorageUploadProvider? uploadProvider = null,
        ILocalTusFileStoreAccessor? tusStoreAccessor = null,
        IConfiguration? configuration = null,
        TimeProvider? timeProvider = null,
        IUploadCommitStorage? commitStorage = null,
        UploadSessionGateRegistry? gateRegistry = null,
        UploadCommitExecutionLeaseManager? executionLeaseManager = null,
        ILogger<PostgreSqlFileStorageService>? logger = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var manager = executionLeaseManager ?? CreateLeaseManager(dbContext, clock);
        return new PostgreSqlFileStorageService(
            dbContext,
            uploadProvider ?? new ServerProxyUploadProvider(),
            tusStoreAccessor!,
            configuration ?? FileStorageTestConfiguration.Default,
            clock,
            commitStorage ?? new NoFinalActionCommitStorage(),
            gateRegistry ?? new UploadSessionGateRegistry(),
            logger ?? NullLogger<PostgreSqlFileStorageService>.Instance,
            manager);
    }

    public static UploadCommitExecutionLeaseManager CreateLeaseManager(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider) =>
        new(
            new UploadCommitExecutionLeaseStore(
                new TestDbContextFactory(
                    (DbContextOptions<ApplicationDbContext>)dbContext.GetService<IDbContextOptions>()),
                timeProvider),
            timeProvider,
            NullLogger<UploadCommitExecutionLeaseManager>.Instance);

    public static IServiceScopeFactory CreateRecoveryScopeFactory(IFileStorageService fileStorageService)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => fileStorageService);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>
    /// 提交存储始终报告“最终存储动作从未开始”，用于只关心 complete 协议、不关心字节面的用例。
    /// </summary>
    internal sealed class NoFinalActionCommitStorage : IUploadCommitStorage
    {
        public Task<UploadCommitStorageResult> CommitAsync(
            UploadCommitIntent intent,
            CancellationToken cancellationToken) =>
            Task.FromResult(UploadCommitStorageResult.ProvenNoFinalActionStarted());
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}
