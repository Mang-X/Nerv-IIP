using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Coding;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Errors;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;

public sealed record InspectionTaskAssignmentResult(
    InspectionTaskId InspectionTaskId,
    string Status,
    string? AssignedInspectorUserId,
    string? AssignedTeamId,
    long Version,
    DateTimeOffset ChangedAtUtc);

public sealed record AssignInspectionTaskCommand(
    InspectionTaskId InspectionTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    string? AssignedInspectorUserId,
    string? AssignedTeamId,
    string? Reason,
    string IdempotencyKey,
    long ExpectedVersion) : ICommand<InspectionTaskAssignmentResult>;

public sealed record ClaimInspectionTaskCommand(
    InspectionTaskId InspectionTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedTeamIds,
    string IdempotencyKey,
    long ExpectedVersion) : ICommand<InspectionTaskAssignmentResult>;

public sealed class AssignInspectionTaskCommandLock : ICommandLock<AssignInspectionTaskCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        AssignInspectionTaskCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InspectionTaskCommandLocks.For(command.InspectionTaskId));
    }
}

public sealed class ClaimInspectionTaskCommandLock : ICommandLock<ClaimInspectionTaskCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        ClaimInspectionTaskCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InspectionTaskCommandLocks.For(command.InspectionTaskId));
    }
}

public static class InspectionTaskCommandLocks
{
    public static CommandLockSettings For(InspectionTaskId inspectionTaskId) =>
        new($"business-quality:inspection-task-submit:{inspectionTaskId}", 30);
}

public sealed class AssignInspectionTaskCommandValidator
    : AbstractValidator<AssignInspectionTaskCommand>
{
    public AssignInspectionTaskCommandValidator()
    {
        RuleFor(x => x.InspectionTaskId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AssignedInspectorUserId).MaximumLength(150);
        RuleFor(x => x.AssignedTeamId).MaximumLength(150);
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x).Must(x =>
            !string.IsNullOrWhiteSpace(x.AssignedInspectorUserId)
            || !string.IsNullOrWhiteSpace(x.AssignedTeamId))
            .WithMessage("An inspector or team assignment is required.");
    }
}

public sealed class ClaimInspectionTaskCommandValidator
    : AbstractValidator<ClaimInspectionTaskCommand>
{
    public ClaimInspectionTaskCommandValidator()
    {
        RuleFor(x => x.InspectionTaskId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(150);
        RuleForEach(x => x.AuthorizedTeamIds).NotEmpty().MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class AssignInspectionTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AssignInspectionTaskCommand, InspectionTaskAssignmentResult>
{
    public async Task<InspectionTaskAssignmentResult> Handle(
        AssignInspectionTaskCommand request,
        CancellationToken cancellationToken)
    {
        var task = await InspectionTaskAssignmentExecution.LoadAsync(
            dbContext,
            request.InspectionTaskId,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        var fingerprint = InspectionTaskAssignmentExecution.Fingerprint(new
        {
            request.ActorPrincipalId,
            request.AssignedInspectorUserId,
            request.AssignedTeamId,
            reason = request.Reason?.Trim(),
            request.ExpectedVersion,
        });
        var replay = await InspectionTaskAssignmentExecution.TryReplayAssignmentAsync(
            dbContext,
            task,
            request.IdempotencyKey,
            fingerprint,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var action = task.AssignedUserId is null && task.AssignedTeamId is null
            ? InspectionTaskAssignmentActions.Assign
            : InspectionTaskAssignmentActions.Transfer;
        if (action == InspectionTaskAssignmentActions.Transfer
            && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new QualityUnprocessableException("transfer-reason-required");
        }

        var previousUser = task.AssignedUserId;
        var previousTeam = task.AssignedTeamId;
        var changedAt = DateTimeOffset.UtcNow;
        try
        {
            task.Assign(
                request.AssignedInspectorUserId,
                request.AssignedTeamId,
                request.ExpectedVersion,
                changedAt);
        }
        catch (ArgumentException exception)
        {
            throw new QualityUnprocessableException(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new QualityLifecycleConflictException(action, exception.Message);
        }

        return InspectionTaskAssignmentExecution.AddReceipt(
            dbContext,
            task,
            action,
            request.IdempotencyKey,
            fingerprint,
            request.ActorPrincipalId,
            previousUser,
            previousTeam,
            request.Reason,
            changedAt);
    }
}

public sealed class ClaimInspectionTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ClaimInspectionTaskCommand, InspectionTaskAssignmentResult>
{
    public async Task<InspectionTaskAssignmentResult> Handle(
        ClaimInspectionTaskCommand request,
        CancellationToken cancellationToken)
    {
        var task = await InspectionTaskAssignmentExecution.LoadAsync(
            dbContext,
            request.InspectionTaskId,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        var fingerprint = InspectionTaskAssignmentExecution.Fingerprint(new
        {
            request.ActorPrincipalId,
            authorizedTeamIds = request.AuthorizedTeamIds
                .Select(x => x.Trim())
                .Order(StringComparer.Ordinal),
            request.ExpectedVersion,
        });
        var replay = await InspectionTaskAssignmentExecution.TryReplayAsync(
            dbContext,
            task,
            InspectionTaskAssignmentActions.Claim,
            request.IdempotencyKey,
            fingerprint,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var previousUser = task.AssignedUserId;
        var previousTeam = task.AssignedTeamId;
        var changedAt = DateTimeOffset.UtcNow;
        try
        {
            task.Claim(
                request.ActorPrincipalId,
                request.AuthorizedTeamIds,
                request.ExpectedVersion,
                changedAt);
        }
        catch (UnauthorizedAccessException)
        {
            throw QualityAuthorizationException.Forbidden(
                "task-outside-selected-work-scope");
        }
        catch (InspectionTaskAlreadyClaimedException)
        {
            throw new QualityUnprocessableException("task-already-claimed");
        }
        catch (InvalidOperationException exception)
        {
            throw new QualityLifecycleConflictException(
                InspectionTaskAssignmentActions.Claim,
                exception.Message);
        }

        return InspectionTaskAssignmentExecution.AddReceipt(
            dbContext,
            task,
            InspectionTaskAssignmentActions.Claim,
            request.IdempotencyKey,
            fingerprint,
            request.ActorPrincipalId,
            previousUser,
            previousTeam,
            null,
            changedAt);
    }
}

internal static class InspectionTaskAssignmentExecution
{
    public static async Task<InspectionTaskAssignmentResult?> TryReplayAssignmentAsync(
        ApplicationDbContext dbContext,
        InspectionTask task,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var normalizedKey = Required(idempotencyKey);
        var receipt = await dbContext.InspectionTaskAssignmentReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OrganizationId == task.OrganizationId
                    && candidate.EnvironmentId == task.EnvironmentId
                    && candidate.InspectionTaskId == task.Id
                    && (candidate.Action == InspectionTaskAssignmentActions.Assign
                        || candidate.Action == InspectionTaskAssignmentActions.Transfer)
                    && candidate.IdempotencyKey == normalizedKey,
                cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        if (!receipt.MatchesPayload(fingerprint))
        {
            throw new QualityIdempotencyConflictException();
        }

        return new InspectionTaskAssignmentResult(
            task.Id,
            InspectionTaskStatuses.Pending,
            receipt.AssignedInspectorUserId,
            receipt.AssignedTeamId,
            receipt.ResultVersion,
            receipt.CreatedAtUtc);
    }

    public static async Task<InspectionTask> LoadAsync(
        ApplicationDbContext dbContext,
        InspectionTaskId taskId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.InspectionTasks.SingleOrDefaultAsync(
            candidate => candidate.Id == taskId,
            cancellationToken);
        if (task is null)
        {
            throw new QualityLifecycleConflictException("inspection-task", "not-found");
        }

        if (!string.Equals(task.OrganizationId, organizationId, StringComparison.Ordinal)
            || !string.Equals(task.EnvironmentId, environmentId, StringComparison.Ordinal))
        {
            throw QualityAuthorizationException.Forbidden("task-tenant-mismatch");
        }

        return task;
    }

    public static async Task<InspectionTaskAssignmentResult?> TryReplayAsync(
        ApplicationDbContext dbContext,
        InspectionTask task,
        string action,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var normalizedKey = Required(idempotencyKey);
        var receipt = await dbContext.InspectionTaskAssignmentReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OrganizationId == task.OrganizationId
                    && candidate.EnvironmentId == task.EnvironmentId
                    && candidate.InspectionTaskId == task.Id
                    && candidate.Action == action
                    && candidate.IdempotencyKey == normalizedKey,
                cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        if (!receipt.MatchesPayload(fingerprint))
        {
            throw new QualityIdempotencyConflictException();
        }

        return new InspectionTaskAssignmentResult(
            task.Id,
            action == InspectionTaskAssignmentActions.Claim
                ? InspectionTaskStatuses.InProgress
                : InspectionTaskStatuses.Pending,
            receipt.AssignedInspectorUserId,
            receipt.AssignedTeamId,
            receipt.ResultVersion,
            receipt.CreatedAtUtc);
    }

    public static InspectionTaskAssignmentResult AddReceipt(
        ApplicationDbContext dbContext,
        InspectionTask task,
        string action,
        string idempotencyKey,
        string fingerprint,
        string actorPrincipalId,
        string? previousUser,
        string? previousTeam,
        string? reason,
        DateTimeOffset changedAt)
    {
        dbContext.InspectionTaskAssignmentReceipts.Add(
            InspectionTaskAssignmentReceipt.Create(
                task.OrganizationId,
                task.EnvironmentId,
                task.Id,
                action,
                Required(idempotencyKey),
                fingerprint,
                actorPrincipalId,
                previousUser,
                previousTeam,
                task.AssignedUserId,
                task.AssignedTeamId,
                reason,
                task.Version,
                changedAt));
        return new InspectionTaskAssignmentResult(
            task.Id,
            task.Status,
            task.AssignedUserId,
            task.AssignedTeamId,
            task.Version,
            changedAt);
    }

    public static string Fingerprint(object payload) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))))
            .ToLowerInvariant();

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be blank.", nameof(value))
            : value.Trim();
}

public static class InspectionTaskAssignmentActions
{
    public const string Assign = "assign";
    public const string Claim = "claim";
    public const string Transfer = "transfer";
}
