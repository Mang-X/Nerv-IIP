using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Scheduling;

public interface IProductEngineeringBusinessDateProvider
{
    DateOnly GetBusinessDate();
}

public sealed class ConfigurationProductEngineeringBusinessDateProvider(
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ConfigurationProductEngineeringBusinessDateProvider> logger)
    : IProductEngineeringBusinessDateProvider
{
    public DateOnly GetBusinessDate()
    {
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ResolveBusinessTimeZone());
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private TimeZoneInfo ResolveBusinessTimeZone()
    {
        var timeZoneId = configuration["ProductEngineering:EngineeringChangeRelease:TimeZoneId"];
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        if (TryFindTimeZone(timeZoneId, out var timeZone))
        {
            return timeZone;
        }

        logger.LogWarning("ProductEngineering engineering change release TimeZoneId '{TimeZoneId}' was not found. Falling back to UTC.", timeZoneId);
        return TimeZoneInfo.Utc;
    }

    private static bool TryFindTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsTimeZoneId) && TryFindNativeTimeZone(windowsTimeZoneId, out timeZone))
        {
            return true;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaTimeZoneId) && TryFindNativeTimeZone(ianaTimeZoneId, out timeZone))
        {
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    private static bool TryFindNativeTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }
}

public sealed class UtcProductEngineeringBusinessDateProvider : IProductEngineeringBusinessDateProvider
{
    public static UtcProductEngineeringBusinessDateProvider Instance { get; } = new();

    private UtcProductEngineeringBusinessDateProvider()
    {
    }

    public DateOnly GetBusinessDate() => DateOnly.FromDateTime(DateTime.UtcNow);
}

public sealed class EngineeringChangeScheduledReleaseService(
    ApplicationDbContext dbContext,
    ILogger<EngineeringChangeScheduledReleaseService>? logger = null)
{
    public async Task<int> PromoteDueReleasesAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        var dueChangeIds = await dbContext.EngineeringChanges
            .Where(x =>
                x.Status == EngineeringVersionStatus.Scheduled &&
                x.EffectiveDate.HasValue &&
                x.EffectiveDate.Value <= businessDate)
            .OrderBy(x => x.OrganizationId)
            .ThenBy(x => x.EnvironmentId)
            .ThenBy(x => x.EffectiveDate)
            .ThenBy(x => x.ChangeNumber)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        if (dueChangeIds.Length == 0)
        {
            return 0;
        }

        var resolver = new ScheduledEngineeringChangeArchiveResolver(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext));
        var promoted = 0;
        foreach (var changeId in dueChangeIds)
        {
            EngineeringChange? change = null;
            try
            {
                change = await dbContext.EngineeringChanges
                    .Include(x => x.AffectedVersions)
                    .SingleOrDefaultAsync(x =>
                        x.Id == changeId &&
                        x.Status == EngineeringVersionStatus.Scheduled,
                        cancellationToken);
                if (change is null)
                {
                    continue;
                }

                var effectiveDate = change.EffectiveDate ?? businessDate;
                var archiveActions = await resolver.ResolveArchiveActionsAsync(change, cancellationToken);
                foreach (var archive in archiveActions)
                {
                    archive(change.ChangeNumber, effectiveDate);
                }

                ProductEngineeringReleaseValidation.AsKnownException(() => change.Release(effectiveDate));
                await dbContext.SaveChangesAsync(cancellationToken);
                promoted++;
                dbContext.ChangeTracker.Clear();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                dbContext.ChangeTracker.Clear();
                logger?.LogError(
                    ex,
                    "ProductEngineering scheduled engineering change release failed for {OrganizationId}/{EnvironmentId}/{ChangeNumber}; it will be retried on the next tick.",
                    change?.OrganizationId,
                    change?.EnvironmentId,
                    change?.ChangeNumber);
            }
        }

        return promoted;
    }
}

public sealed class EngineeringChangeScheduledReleaseScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<EngineeringChangeScheduledReleaseScheduler> logger,
    IProductEngineeringBusinessDateProvider businessDateProvider)
    : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("ProductEngineering:EngineeringChangeRelease:Enabled", true))
        {
            return;
        }

        var interval = configuration.GetValue("ProductEngineering:EngineeringChangeRelease:Interval", DefaultInterval);
        using var timer = new PeriodicTimer(interval);
        await TryPromoteAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryPromoteAsync(stoppingToken);
        }
    }

    private async Task TryPromoteAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<EngineeringChangeScheduledReleaseService>();
            var promoted = await service.PromoteDueReleasesAsync(businessDateProvider.GetBusinessDate(), cancellationToken);
            if (promoted > 0)
            {
                logger.LogInformation("Promoted {PromotedCount} due ProductEngineering engineering changes.", promoted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProductEngineering scheduled engineering change release tick failed; the scheduler will retry on the next tick.");
        }
    }
}

internal sealed class ScheduledEngineeringChangeArchiveResolver(
    IEngineeringBomRepository engineeringBomRepository,
    IManufacturingBomRepository manufacturingBomRepository,
    IRoutingRepository routingRepository,
    IProductionVersionRepository productionVersionRepository)
{
    public async Task<IReadOnlyCollection<Action<string, DateOnly>>> ResolveArchiveActionsAsync(
        EngineeringChange change,
        CancellationToken cancellationToken)
    {
        var actions = new List<Action<string, DateOnly>>();
        foreach (var affectedVersion in change.AffectedVersions)
        {
            actions.Add(await ResolveAffectedVersionAsync(change.OrganizationId, change.EnvironmentId, affectedVersion, cancellationToken));
        }

        return actions;
    }

    private async Task<Action<string, DateOnly>> ResolveAffectedVersionAsync(
        string organizationId,
        string environmentId,
        EngineeringChangeAffectedVersion affectedVersion,
        CancellationToken cancellationToken)
    {
        return affectedVersion.VersionKind.Trim().ToLowerInvariant() switch
        {
            "engineering-bom" => ArchiveEngineeringBom(await engineeringBomRepository.GetByVersionIdAsync(
                organizationId,
                environmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId, await GetSuccessorEngineeringBomAsync(organizationId, environmentId, affectedVersion, cancellationToken)),
            "manufacturing-bom" => ArchiveManufacturingBom(await manufacturingBomRepository.GetByVersionIdAsync(
                organizationId,
                environmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId, await GetSuccessorManufacturingBomAsync(organizationId, environmentId, affectedVersion, cancellationToken)),
            "routing" => ArchiveRouting(await routingRepository.GetByVersionIdAsync(
                organizationId,
                environmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId, await GetSuccessorRoutingAsync(organizationId, environmentId, affectedVersion, cancellationToken)),
            "production-version" => ArchiveProductionVersion(await productionVersionRepository.GetByIdAsync(
                organizationId,
                environmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId, await GetSuccessorProductionVersionAsync(organizationId, environmentId, affectedVersion, cancellationToken)),
            _ => throw new KnownException($"Affected version kind '{affectedVersion.VersionKind}' is not supported.")
        };
    }

    private async Task<EngineeringBom?> GetSuccessorEngineeringBomAsync(
        string organizationId,
        string environmentId,
        EngineeringChangeAffectedVersion affectedVersion,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(affectedVersion.SupersededByVersionId)
            ? null
            : await engineeringBomRepository.GetByVersionIdAsync(
                organizationId,
                environmentId,
                affectedVersion.SupersededByVersionId,
                cancellationToken)
            ?? throw new KnownException($"Successor engineering BOM version '{affectedVersion.SupersededByVersionId}' was not found.");
    }

    private async Task<ManufacturingBom?> GetSuccessorManufacturingBomAsync(
        string organizationId,
        string environmentId,
        EngineeringChangeAffectedVersion affectedVersion,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(affectedVersion.SupersededByVersionId)
            ? null
            : await manufacturingBomRepository.GetByVersionIdAsync(
                organizationId,
                environmentId,
                affectedVersion.SupersededByVersionId,
                cancellationToken)
            ?? throw new KnownException($"Successor manufacturing BOM version '{affectedVersion.SupersededByVersionId}' was not found.");
    }

    private async Task<Routing?> GetSuccessorRoutingAsync(
        string organizationId,
        string environmentId,
        EngineeringChangeAffectedVersion affectedVersion,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(affectedVersion.SupersededByVersionId)
            ? null
            : await routingRepository.GetByVersionIdAsync(
                organizationId,
                environmentId,
                affectedVersion.SupersededByVersionId,
                cancellationToken)
            ?? throw new KnownException($"Successor routing version '{affectedVersion.SupersededByVersionId}' was not found.");
    }

    private async Task<ProductionVersion?> GetSuccessorProductionVersionAsync(
        string organizationId,
        string environmentId,
        EngineeringChangeAffectedVersion affectedVersion,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(affectedVersion.SupersededByVersionId)
            ? null
            : await productionVersionRepository.GetByIdAsync(
                organizationId,
                environmentId,
                affectedVersion.SupersededByVersionId,
                cancellationToken)
            ?? throw new KnownException($"Successor production version '{affectedVersion.SupersededByVersionId}' was not found.");
    }

    private static Action<string, DateOnly> ArchiveEngineeringBom(EngineeringBom? bom, string versionId, EngineeringBom? successor)
    {
        if (bom is not null && successor is not null)
        {
            EnsurePublishedSuccessor(successor.Status, successor.BomCode == bom.BomCode, "engineering BOM", successor.BomCode, versionId);
        }

        return bom is null
            ? throw new KnownException($"Engineering BOM version '{versionId}' was not found.")
            : (reason, _) => ProductEngineeringReleaseValidation.AsKnownException(() => bom.Archive(reason));
    }

    private static Action<string, DateOnly> ArchiveManufacturingBom(ManufacturingBom? bom, string versionId, ManufacturingBom? successor)
    {
        if (bom is not null && successor is not null)
        {
            EnsurePublishedSuccessor(successor.Status, successor.BomCode == bom.BomCode, "manufacturing BOM", successor.BomCode, versionId);
        }

        return bom is null
            ? throw new KnownException($"Manufacturing BOM version '{versionId}' was not found.")
            : (reason, _) => ProductEngineeringReleaseValidation.AsKnownException(() => bom.Archive(reason));
    }

    private static Action<string, DateOnly> ArchiveRouting(Routing? routing, string versionId, Routing? successor)
    {
        if (routing is not null && successor is not null)
        {
            EnsurePublishedSuccessor(successor.Status, successor.RoutingCode == routing.RoutingCode, "routing", successor.RoutingCode, versionId);
        }

        return routing is null
            ? throw new KnownException($"Routing version '{versionId}' was not found.")
            : (reason, _) => ProductEngineeringReleaseValidation.AsKnownException(() => routing.Archive(reason));
    }

    private static Action<string, DateOnly> ArchiveProductionVersion(ProductionVersion? version, string versionId, ProductionVersion? successor)
    {
        if (version is not null && successor is not null)
        {
            EnsureActiveSuccessor(successor, version, versionId);
        }

        return version is null
            ? throw new KnownException($"Production version '{versionId}' was not found.")
            : successor is null
                ? (reason, _) => ProductEngineeringReleaseValidation.AsKnownException(() => version.Archive(reason))
                : (reason, effectiveDate) => ProductEngineeringReleaseValidation.AsKnownException(() => version.SupersedeWith(successor, effectiveDate, reason));
    }

    private static void EnsurePublishedSuccessor(EngineeringVersionStatus status, bool sameBusinessCode, string versionKind, string successorCode, string versionId)
    {
        if (status != EngineeringVersionStatus.Published || !sameBusinessCode)
        {
            throw new KnownException($"Successor {versionKind} version '{successorCode}' must be published for the same code before it can supersede '{versionId}'.");
        }
    }

    private static void EnsureActiveSuccessor(ProductionVersion successor, ProductionVersion version, string versionId)
    {
        if (successor.Status != ProductionVersionStatus.Active || successor.SkuCode != version.SkuCode)
        {
            throw new KnownException($"Successor production version '{successor.Id.Id:D}' must be active for the same SKU before it can supersede '{versionId}'.");
        }
    }
}
