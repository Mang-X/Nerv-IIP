using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record CreateMasterProductionScheduleBucketCommand(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly BucketDate,
    decimal Quantity,
    string? IdempotencyKey = null) : ICommand<MasterProductionScheduleId>;

public sealed record UpdateMasterProductionScheduleBucketCommand(
    string OrganizationId,
    string EnvironmentId,
    MasterProductionScheduleId MpsId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    DateOnly BucketDate,
    decimal Quantity) : ICommand;

public sealed record ReviewMasterProductionScheduleBucketCommand(
    string OrganizationId,
    string EnvironmentId,
    MasterProductionScheduleId MpsId,
    string ReviewedBy) : ICommand;

public sealed record ReleaseMasterProductionScheduleBucketCommand(
    string OrganizationId,
    string EnvironmentId,
    MasterProductionScheduleId MpsId,
    string ReleasedBy) : ICommand;

public sealed class CreateMasterProductionScheduleBucketCommandValidator
    : AbstractValidator<CreateMasterProductionScheduleBucketCommand>
{
    public CreateMasterProductionScheduleBucketCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).MaximumLength(128);
    }
}

public sealed class UpdateMasterProductionScheduleBucketCommandValidator
    : AbstractValidator<UpdateMasterProductionScheduleBucketCommand>
{
    public UpdateMasterProductionScheduleBucketCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.MpsId).NotEmpty();
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class ReviewMasterProductionScheduleBucketCommandValidator
    : AbstractValidator<ReviewMasterProductionScheduleBucketCommand>
{
    public ReviewMasterProductionScheduleBucketCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.MpsId).NotEmpty();
        RuleFor(x => x.ReviewedBy).NotEmpty().MaximumLength(128);
    }
}

public sealed class ReleaseMasterProductionScheduleBucketCommandValidator
    : AbstractValidator<ReleaseMasterProductionScheduleBucketCommand>
{
    public ReleaseMasterProductionScheduleBucketCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.MpsId).NotEmpty();
        RuleFor(x => x.ReleasedBy).NotEmpty().MaximumLength(128);
    }
}

public sealed class CreateMasterProductionScheduleBucketCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateMasterProductionScheduleBucketCommand, MasterProductionScheduleId>
{
    public async Task<MasterProductionScheduleId> Handle(
        CreateMasterProductionScheduleBucketCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.MasterProductionSchedules.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.SkuCode == request.SkuCode
            && x.SiteCode == request.SiteCode
            && x.BucketDate == request.BucketDate,
            cancellationToken);
        if (existing is not null)
        {
            existing.Update(request.SkuCode, request.UomCode, request.SiteCode, request.BucketDate, request.Quantity);
            return existing.Id;
        }

        var bucket = MasterProductionSchedule.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.BucketDate,
            request.Quantity);
        dbContext.MasterProductionSchedules.Add(bucket);
        return bucket.Id;
    }
}

public sealed class UpdateMasterProductionScheduleBucketCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<UpdateMasterProductionScheduleBucketCommand>
{
    public async Task Handle(UpdateMasterProductionScheduleBucketCommand request, CancellationToken cancellationToken)
    {
        var bucket = await MasterProductionScheduleCommandLoader.LoadBucketAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.MpsId,
            cancellationToken);
        bucket.Update(request.SkuCode, request.UomCode, request.SiteCode, request.BucketDate, request.Quantity);
    }
}

public sealed class ReviewMasterProductionScheduleBucketCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReviewMasterProductionScheduleBucketCommand>
{
    public async Task Handle(ReviewMasterProductionScheduleBucketCommand request, CancellationToken cancellationToken)
    {
        var bucket = await MasterProductionScheduleCommandLoader.LoadBucketAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.MpsId,
            cancellationToken);
        bucket.MarkReviewed(request.ReviewedBy);
    }
}

public sealed class ReleaseMasterProductionScheduleBucketCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReleaseMasterProductionScheduleBucketCommand>
{
    public async Task Handle(ReleaseMasterProductionScheduleBucketCommand request, CancellationToken cancellationToken)
    {
        var bucket = await MasterProductionScheduleCommandLoader.LoadBucketAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.MpsId,
            cancellationToken);
        bucket.Release(request.ReleasedBy);
    }
}

file static class MasterProductionScheduleCommandLoader
{
    public static async Task<MasterProductionSchedule> LoadBucketAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        MasterProductionScheduleId mpsId,
        CancellationToken cancellationToken)
    {
        return await dbContext.MasterProductionSchedules.SingleOrDefaultAsync(x =>
            x.Id == mpsId
            && x.OrganizationId == organizationId
            && x.EnvironmentId == environmentId,
            cancellationToken)
            ?? throw new KnownException($"MPS bucket was not found, MpsId = {mpsId}");
    }
}
