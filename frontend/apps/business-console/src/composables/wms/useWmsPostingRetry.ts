import {
  retryBusinessConsoleWmsInboundInventoryPosting,
  retryBusinessConsoleWmsOutboundInventoryPosting,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
  preserveBusinessWriteErrorStatus,
} from '@nerv-iip/business-core'
import { shallowRef } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { createWmsIdempotencyKey, trustedWorkScope, type WmsListFilters } from './wmsShared'

export function useWmsPostingRetry(
  direction: 'inbound' | 'outbound',
  filters: WmsListFilters,
  refresh: () => Promise<unknown>,
) {
  const auth = useAuthStore()
  const pending = shallowRef(false)

  async function retry(orderId: string) {
    if (pending.value) return
    pending.value = true
    try {
      const workScope = trustedWorkScope(filters)
      const query = { organizationId: filters.organizationId, environmentId: filters.environmentId }
      const intent = {
        ...query,
        principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
        operationType: `wms.${direction}-order.inventory-posting.retry`,
        payloadFingerprint: JSON.stringify([orderId, workScope.scopeKind, workScope.scopeId]),
      }
      await completePendingBusinessIntent(intent, async () => {
        const { idempotencyKey } = acquirePendingBusinessIntent(intent, createWmsIdempotencyKey)
        const options = {
          query,
          body: { ...workScope, idempotencyKey },
          throwOnError: false as const,
        }
        const { data, error, response } =
          direction === 'inbound'
            ? await retryBusinessConsoleWmsInboundInventoryPosting({
                ...options,
                path: { inboundOrderId: orderId },
              })
            : await retryBusinessConsoleWmsOutboundInventoryPosting({
                ...options,
                path: { outboundOrderId: orderId },
              })
        if (!data?.success || !data.data?.requestId) {
          const failure = error ?? data ?? new Error('重试请求未返回库存过账受理结果。')
          preserveBusinessWriteErrorStatus(failure, response?.status)
          throw failure
        }
      })
      notifySuccess('已提交库存过账重试，请查看原单过账状态。')
    } catch (error) {
      notifyOperationFailure('提交过账重试失败', error, '暂未确认过账重试结果，请刷新原单。')
    } finally {
      try {
        await refresh()
      } finally {
        pending.value = false
      }
    }
  }

  return { retry, pending }
}
