using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

/// <summary>
/// Registers a factory worker. <paramref name="UserId"/> is the stable person identifier reused by
/// team membership, personnel skills and MES dispatch; when omitted it is derived from the allocated
/// employee number so the two never drift apart.
/// </summary>
public sealed record CreateWorkerCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string? UserId,
    string? DepartmentCode,
    string? JobTitle,
    string? EmploymentStatus,
    string? Phone,
    string? IdempotencyKey = null,
    MasterDataIntegrationEventContext? AuditContext = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateWorkerCommandHandler(
    IWorkerRepository repository,
    MasterDataCodingService? codingService = null,
    ApplicationDbContext? dbContext = null)
    : ICommandHandler<CreateWorkerCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateWorkerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var employmentStatus = Worker.NormalizeStatus(request.EmploymentStatus);
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "worker",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.UserId, request.DepartmentCode, request.JobTitle, employmentStatus),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("worker", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"人员 '{code}' 已存在。");
        }

        var userId = string.IsNullOrWhiteSpace(request.UserId) ? code : request.UserId.Trim();
        if (await repository.UserIdTakenAsync(request.OrganizationId, request.EnvironmentId, userId, cancellationToken))
        {
            throw new KnownException($"人员身份 '{userId}' 已登记。");
        }

        var worker = Worker.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            userId,
            request.DepartmentCode,
            request.JobTitle,
            employmentStatus,
            request.Phone);
        await repository.AddAsync(worker, cancellationToken);
        MasterDataScopeContextAudit.AddCreated(
            dbContext ?? throw new KnownException("创建人员需要范围审计存储。"),
            request.AuditContext,
            request.OrganizationId,
            request.EnvironmentId,
            "worker",
            worker.Id.ToString(),
            worker.Code,
            new
            {
                userId = worker.UserId,
                employmentStatus = worker.EmploymentStatus,
                disabled = worker.Disabled,
            });
        return new MasterDataResourceResult("worker", worker.Code, worker.Name);
    }
}
