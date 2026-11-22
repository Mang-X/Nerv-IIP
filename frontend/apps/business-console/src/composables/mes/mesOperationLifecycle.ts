import {
  listBusinessConsoleMesOperationTasks,
  listBusinessConsoleMesReportableOperationTasks,
} from '@nerv-iip/api-client'
import { DEFAULT_TAKE, type MesContextFilters } from './mesShared'
import { type MesSelectedWorkScope } from './useMesWorkScope'

export async function readMesOperationLifecycleRequest(
  operationTaskId: string,
  context: MesContextFilters,
  selectedScope: MesSelectedWorkScope,
  action: 'start' | 'pause' | 'resume' | 'complete' | 'report-complete',
  idempotentReplay = false,
) {
  const workOrderId = context.workOrderId?.trim()
  if (!workOrderId) return undefined

  let skip = 0
  while (true) {
    const request = {
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        workOrderId,
        scopeKind: selectedScope.kind,
        scopeId: selectedScope.id,
        skip,
        take: DEFAULT_TAKE,
      },
      throwOnError: false,
    } as const
    const response =
      action === 'report-complete'
        ? await listBusinessConsoleMesReportableOperationTasks(request)
        : await listBusinessConsoleMesOperationTasks(request)
    if (response.error !== undefined) throw response.error
    if (!response.data?.success) throw response.data ?? new Error('读取工序最新状态失败')
    const items = response.data.data?.items ?? []
    const item = items.find((candidate) => candidate.operationTaskId === operationTaskId)
    if (item) {
      return {
        domain: 'mes-operation-task' as const,
        action,
        facts: { status: item.status, idempotentReplay },
      }
    }
    const total = response.data.data?.total ?? 0
    if (items.length === 0 || skip + items.length >= total) return undefined
    skip += items.length
  }
}
