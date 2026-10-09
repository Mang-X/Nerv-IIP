using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Concurrency;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

public sealed record ConfirmLabelPrintBatchCommand(
    LabelPrintBatchId PrintBatchId,
    string OrganizationId,
    string EnvironmentId) : ICommand<LabelPrintBatchId>;

public sealed class ConfirmLabelPrintBatchCommandValidator : AbstractValidator<ConfirmLabelPrintBatchCommand>
{
    public ConfirmLabelPrintBatchCommandValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class ConfirmLabelPrintBatchCommandHandler(
    ApplicationDbContext dbContext,
    ILabelPrintBatchActivationFence activationFence)
    : ICommandHandler<ConfirmLabelPrintBatchCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(
        ConfirmLabelPrintBatchCommand request,
        CancellationToken cancellationToken)
    {
        var organizationId = BarcodeLabelText.Required(request.OrganizationId, nameof(request.OrganizationId));
        var environmentId = BarcodeLabelText.Required(request.EnvironmentId, nameof(request.EnvironmentId));
        // 与 MES 激活共享同一批次事务锁，状态推进只有一个赢家。
        await activationFence.AcquireAsync(organizationId, environmentId, request.PrintBatchId, cancellationToken);
        var batch = await dbContext.LabelPrintBatches.SingleOrDefaultAsync(
            x => x.Id == request.PrintBatchId
                && x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId,
            cancellationToken) ?? throw new KnownException("未找到当前组织和环境内的打印批次。");
        try
        {
            batch.ConfirmReadyToPrint();
        }
        catch (LabelPrintLifecycleRejectedException exception)
        {
            throw LabelPrintLifecycleKnownExceptionMapper.Create(exception, sequenceNo: 0);
        }

        return batch.Id;
    }
}
