import type { MesReportExecutionContext, RecordReportInput } from './mesShared'
import type { PendingBusinessIntentScope } from '@nerv-iip/business-core'

export function mesReportIntentScope(
  context: MesReportExecutionContext,
  input: RecordReportInput,
): PendingBusinessIntentScope {
  const { idempotencyKey: _key, ...payload } = input
  return {
    principalId: context.principalId,
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    operationType: 'mes.production-report.record',
    payloadFingerprint: JSON.stringify({
      ...payload,
      scopeKind: context.scopeKind,
      scopeId: context.scopeId,
    }),
  }
}
