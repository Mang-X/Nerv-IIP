import {
  getBusinessConsoleWmsCountWorkScopesQueryOptions,
  getBusinessConsoleWmsReceiptWorkScopesQueryOptions,
  getBusinessConsoleWmsShipmentWorkScopesQueryOptions,
  type BusinessConsoleWmsWorkScopeCatalogEnvelope,
  type BusinessConsoleWmsWorkScopeCatalogItem,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive, watch } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { inlineErrorMessage } from '@/utils/notify'

const SUPPORTED_SCOPE_KINDS = new Set(['self', 'work-pool', 'site'])

export type WmsWorkScopeCatalogKind = 'receipts' | 'shipments' | 'counts'

export interface WmsWorkScopeOption {
  label: string
  value: string
}

export interface WmsWorkScopeFilters {
  scopeKind?: string
  scopeId?: string
  skip: number
}

function scopeValue(kind: string, id: string) {
  return `${kind}:${id}`
}

function parseScope(value: string | undefined) {
  if (!value) return undefined
  const separator = value.indexOf(':')
  if (separator <= 0 || separator === value.length - 1) return undefined
  return { kind: value.slice(0, separator), id: value.slice(separator + 1) }
}

function normalizeScope(
  item: BusinessConsoleWmsWorkScopeCatalogItem,
): Required<Pick<BusinessConsoleWmsWorkScopeCatalogItem, 'scopeKind' | 'scopeId'>> &
  BusinessConsoleWmsWorkScopeCatalogItem {
  return {
    ...item,
    scopeKind: item.scopeKind?.trim() ?? '',
    scopeId: item.scopeId?.trim() ?? '',
  }
}

// 同一 principal/org/env/作业域的范围选择在整个 Console 共享，用户显式切换会记住
// （localStorage）；自动兜底选择不写入，避免把兜底固化成偏好（与 MES #1288 同姿势）。
const WMS_WORK_SCOPE_STORAGE_PREFIX = 'nerv-iip.business-console.wms-work-scope.v1'
const sharedWmsWorkScopeSelections = reactive(new Map<string, string>())

function readRememberedWmsWorkScope(selectionKey: string) {
  try {
    return (
      globalThis.localStorage?.getItem(`${WMS_WORK_SCOPE_STORAGE_PREFIX}:${selectionKey}`) ??
      undefined
    )
  } catch {
    return undefined
  }
}

function writeRememberedWmsWorkScope(selectionKey: string, value: string) {
  try {
    globalThis.localStorage?.setItem(`${WMS_WORK_SCOPE_STORAGE_PREFIX}:${selectionKey}`, value)
  } catch {
    // 持久化失败只影响「记住选择」，不影响本次会话内的共享选择。
  }
}

export function useWmsWorkScope(catalog: WmsWorkScopeCatalogKind) {
  const context = useBusinessContextStore()
  const auth = useAuthStore()
  const organizationId = computed(() => context.organizationId)
  const environmentId = computed(() => context.environmentId)
  const hasTenant = computed(() => Boolean(organizationId.value && environmentId.value))

  const catalogQuery = useQuery(() => {
    const options = {
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
      },
    }
    const queryOptions =
      catalog === 'receipts'
        ? getBusinessConsoleWmsReceiptWorkScopesQueryOptions(options)
        : catalog === 'shipments'
          ? getBusinessConsoleWmsShipmentWorkScopesQueryOptions(options)
          : getBusinessConsoleWmsCountWorkScopesQueryOptions(options)

    return {
      ...queryOptions,
      enabled: hasTenant.value,
    }
  })

  const envelope = computed(
    () => catalogQuery.data.value as BusinessConsoleWmsWorkScopeCatalogEnvelope | undefined,
  )
  const catalogData = computed(() =>
    envelope.value?.success === true && envelope.value.data ? envelope.value.data : undefined,
  )
  const authorizedScopes = computed(() =>
    (catalogData.value?.items ?? [])
      .map(normalizeScope)
      .filter(
        (scope) =>
          Boolean(scope.scopeKind && scope.scopeId) && SUPPORTED_SCOPE_KINDS.has(scope.scopeKind),
      ),
  )
  const scopeOptions = computed<WmsWorkScopeOption[]>(() =>
    authorizedScopes.value.map((scope) => ({
      label: scope.scopeKind === 'self' ? '我的任务' : (scope.displayName?.trim() ?? scope.scopeId),
      value: scopeValue(scope.scopeKind, scope.scopeId),
    })),
  )

  const selectionKey = computed(() =>
    [
      auth.principal?.principalId?.trim() || auth.sessionId?.trim() || 'unrestored-session',
      organizationId.value,
      environmentId.value,
      catalog,
    ].join('|'),
  )

  // 生效选择整条由「授权清单 × 当前 selectionKey 的记忆」推导，不留可变的本地选择状态：
  // 用 watch + 「旧选择仍合法就 early-return」会在 principal/org/env 切换后继续沿用上一个
  // 主体的选择，永远读不到新 principal 的记忆（与 MES 同姿势的 computed 推导）。
  const selectedScopeKey = computed<string | undefined>({
    get: () => {
      const options = scopeOptions.value
      if (options.length === 0) return undefined
      const remembered =
        sharedWmsWorkScopeSelections.get(selectionKey.value) ??
        readRememberedWmsWorkScope(selectionKey.value)
      // 记住的选择仍在授权清单里才作数；否则回落清单首项，兜底不写入记忆。
      return options.some((option) => option.value === remembered) ? remembered : options[0]?.value
    },
    set: (value) => {
      if (!value) return
      sharedWmsWorkScopeSelections.set(selectionKey.value, value)
      writeRememberedWmsWorkScope(selectionKey.value, value)
    },
  })
  const scopeKey = selectedScopeKey

  const parsedSelection = computed(() => parseScope(selectedScopeKey.value))
  const hasSelection = computed(
    () =>
      hasTenant.value &&
      scopeOptions.value.some((option) => option.value === selectedScopeKey.value),
  )

  /**
   * 范围未就绪的**真实原因**，绝不含糊成「请稍后重试」。
   *
   * 无授权范围时服务端是直接 403（`missing-exact-site-grant`），不是回一份空目录，
   * 所以「去 IAM 配置」这句话并进 403 分支——不为一个到不了的分支单留文案与标志位。
   */
  const unreadyMessage = computed(() => {
    if (!hasTenant.value) return '请先选择组织与环境，再查看作业范围。'
    if (catalogQuery.error.value) {
      return `取不到已授权的作业范围：${inlineErrorMessage(
        catalogQuery.error.value,
        '作业范围目录未成功返回。',
      )}若是权限不足，请到 IAM 为本账号配置站点授权或作业池成员资格。`
    }
    if (catalogQuery.isLoading.value || envelope.value === undefined) {
      return '正在获取已授权的作业范围…'
    }
    if (envelope.value.success !== true || scopeOptions.value.length === 0) {
      return '作业范围目录未返回任何已授权范围，请到 IAM 为本账号配置站点授权或作业池成员资格。'
    }
    return hasSelection.value ? '' : '请先在上方选择作业范围。'
  })

  return {
    organizationId,
    environmentId,
    principalId: computed(() => catalogData.value?.actorPrincipalId?.trim() ?? ''),
    hasTenant,
    hasSelection,
    unreadyMessage,
    scopeKey,
    scopeKind: computed(() => (hasSelection.value ? parsedSelection.value?.kind : undefined)),
    scopeId: computed(() => (hasSelection.value ? parsedSelection.value?.id : undefined)),
    scopeOptions,
    selectedScopeLabel: computed(
      () =>
        scopeOptions.value.find((option) => option.value === selectedScopeKey.value)?.label ?? '',
    ),
    pending: catalogQuery.isLoading,
    error: catalogQuery.error,
    refresh: () => (hasTenant.value ? catalogQuery.refetch() : Promise.resolve()),
  }
}

export function bindWmsWorkScopeFilters<TFilters extends WmsWorkScopeFilters>(
  filters: TFilters,
  catalog: WmsWorkScopeCatalogKind,
) {
  const scope = useWmsWorkScope(catalog)

  watch(
    () => [scope.scopeKind.value, scope.scopeId.value] as const,
    ([scopeKind, scopeId]) => {
      filters.scopeKind = scopeKind
      filters.scopeId = scopeId
      filters.skip = 0
    },
    { immediate: true, flush: 'sync' },
  )

  return scope
}
