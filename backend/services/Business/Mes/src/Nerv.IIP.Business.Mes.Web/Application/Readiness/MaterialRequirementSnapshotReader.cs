using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Readiness;

/// <summary>
/// The only online read boundary for frozen material-requirement snapshots. It always selects a
/// complete latest capture per work order before resolving tracked copies or applying caller scope.
/// </summary>
internal static class MaterialRequirementSnapshotReader
{
    internal static async Task<MaterialRequirementSnapshotCapture> LoadLatestByWorkOrderAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        var captures = await LoadLatestCapturesAsync(
            dbContext,
            organizationId,
            environmentId,
            [workOrderId],
            cancellationToken);
        return captures.SingleOrDefault()
            ?? new MaterialRequirementSnapshotCapture(workOrderId, null, []);
    }

    internal static async Task<MaterialRequirementSnapshot[]> LoadLatestByWorkOrdersAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> workOrderIds,
        CancellationToken cancellationToken) =>
        (await LoadLatestCapturesAsync(
            dbContext,
            organizationId,
            environmentId,
            workOrderIds,
            cancellationToken))
        .SelectMany(x => x.Requirements)
        .ToArray();

    private static async Task<MaterialRequirementSnapshotCapture[]> LoadLatestCapturesAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> workOrderIds,
        CancellationToken cancellationToken)
    {
        var persisted = await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .Select(x => new MaterialRequirementSnapshotCandidate(
                x.Id,
                x.WorkOrderId,
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.RequiredQuantity,
                x.AvailableQuantity,
                x.StagedQuantity,
                x.SourceSystem,
                x.SourceSnapshotId,
                x.CapturedAtUtc,
                x.SubstituteMaterialIdsJson,
                IsLocal: false))
            .ToArrayAsync(cancellationToken);
        var local = dbContext.MaterialRequirements.Local
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .Select(x => new MaterialRequirementSnapshotCandidate(
                x.Id,
                x.WorkOrderId,
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.RequiredQuantity,
                x.AvailableQuantity,
                x.StagedQuantity,
                x.SourceSystem,
                x.SourceSnapshotId,
                x.CapturedAtUtc,
                x.SubstituteMaterialIdsJson,
                IsLocal: true));

        return persisted
            .Concat(local)
            .GroupBy(x => x.WorkOrderId, StringComparer.Ordinal)
            .Select(group =>
            {
                var captureIdentity = group.Max(x => x.CapturedAtUtc);
                var requirements = group
                    .Where(x => x.CapturedAtUtc == captureIdentity)
                    .GroupBy(x => x.Id)
                    .Select(identityGroup => identityGroup.OrderBy(x => x.IsLocal).Last())
                    .Select(ToSnapshot)
                    .ToArray();
                return new MaterialRequirementSnapshotCapture(group.Key, captureIdentity, requirements);
            })
            .ToArray();
    }

    private static MaterialRequirementSnapshot ToSnapshot(MaterialRequirementSnapshotCandidate candidate) =>
        new(
            candidate.Id,
            candidate.WorkOrderId,
            candidate.OperationTaskId,
            candidate.MaterialId,
            candidate.MaterialLotId,
            candidate.RequiredQuantity,
            candidate.AvailableQuantity,
            candidate.StagedQuantity,
            candidate.SourceSystem,
            candidate.SourceSnapshotId,
            candidate.CapturedAtUtc,
            JsonSerializer.Deserialize<string[]>(candidate.SubstituteMaterialIdsJson) ?? []);

    private sealed record MaterialRequirementSnapshotCandidate(
        MaterialRequirementId Id,
        string WorkOrderId,
        string? OperationTaskId,
        string MaterialId,
        string? MaterialLotId,
        decimal RequiredQuantity,
        decimal AvailableQuantity,
        decimal StagedQuantity,
        string SourceSystem,
        string SourceSnapshotId,
        DateTimeOffset CapturedAtUtc,
        string SubstituteMaterialIdsJson,
        bool IsLocal);
}

internal sealed record MaterialRequirementSnapshotCapture(
    string WorkOrderId,
    DateTimeOffset? CaptureIdentity,
    MaterialRequirementSnapshot[] Requirements);

internal sealed record MaterialRequirementSnapshot(
    MaterialRequirementId Id,
    string WorkOrderId,
    string? OperationTaskId,
    string MaterialId,
    string? MaterialLotId,
    decimal RequiredQuantity,
    decimal AvailableQuantity,
    decimal StagedQuantity,
    string SourceSystem,
    string SourceSnapshotId,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyCollection<string> SubstituteMaterialIds);
