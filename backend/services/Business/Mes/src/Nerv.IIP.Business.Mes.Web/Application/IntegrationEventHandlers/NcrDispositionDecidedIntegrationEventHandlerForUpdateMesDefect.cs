using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.NcrDispositionDecidedIntegrationEvent", ConsumerName)]
public sealed class NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<NcrDispositionDecidedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.quality-ncr-disposition";

    private readonly IntegrationEventConsumerGuard<NcrDispositionDecidedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            QualityIntegrationEventTypes.DispositionDecided,
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(NcrDispositionDecidedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe(nameof(NcrDispositionDecidedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(NcrDispositionDecidedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(NcrDispositionDecidedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var defectNo = integrationEvent.Payload.SourceDocumentId;
        if (string.IsNullOrWhiteSpace(defectNo))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        defectNo = defectNo.Trim();
        var defect = await dbContext.DefectRecords.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.DefectNo == defectNo,
            cancellationToken);
        if (defect is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var referenceId = integrationEvent.Payload.DispositionType.Trim().ToLowerInvariant() switch
        {
            QualityNcrDispositionTypes.Rework => integrationEvent.Payload.ReworkWorkOrderId,
            QualityNcrDispositionTypes.Scrap => integrationEvent.Payload.ScrapMovementId,
            QualityNcrDispositionTypes.ReturnToSupplier => integrationEvent.Payload.ReturnDocumentId,
            QualityNcrDispositionTypes.ConditionalRelease or QualityNcrDispositionTypes.SortAndScreen => null,
            _ => null,
        };

        // #3318：referenceId 是 Quality 的处置引用身份，产出列宽 150；它被 AcceptDisposition 逐字
        // 写进 defect_records.disposition_reference_id。改前该列只有 100，101–150 字符的
        // 合法引用在 SaveChangesAsync 抛 DbUpdateException(22001)——而这个 handler 函数体内一条 catch
        // 都没有，异常直接逃逸出 HandleAsync 变成 poison message（#877），整条消费链卡死。
        // 这里就地判长并走死信：既不截断（截断会静默伪造一个指不到任何对象的下游引用，
        // 而行上没有任何字段记录它被截断过），也不抛出（抛出就是回到 poison message）。
        // 列宽已加宽到与 Quality 产出列一致，因此今天合法的引用走不到这条分支；
        // 它看守的是「将来任一侧列宽再变」。
        //
        // **归一化只此一处**：DefectRecord.AcceptDisposition 已不再自己 Trim（见那边的参数注释），
        // 原样落库。因此「守卫量的字符串 == 落库的字符串」是构造上成立的，
        // 不依赖「两处归一化必须保持幂等等价」这条约定——那条约定一旦被谁改分叉，没有任何东西会红。
        var normalizedReferenceId = string.IsNullOrWhiteSpace(referenceId) ? null : referenceId.Trim();
        if (normalizedReferenceId is not null && MesDefectDispositionReferenceIdPolicy.ExceedsColumn(normalizedReferenceId))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    MesDefectDispositionReferenceIdPolicy.OverlongFailureCode,
                    MesDefectDispositionReferenceIdPolicy.OverlongFailureMessage(normalizedReferenceId)),
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        defect.AcceptDisposition(
            integrationEvent.Payload.NcrId,
            integrationEvent.Payload.NcrCode,
            integrationEvent.Payload.DispositionType,
            normalizedReferenceId,
            integrationEvent.Payload.ChangedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
