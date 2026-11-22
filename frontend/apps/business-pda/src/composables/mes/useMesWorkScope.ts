import { getBusinessConsolePrincipalWorkContextQueryOptions } from '@nerv-iip/api-client'
import { formatWorkScopeKey, parseWorkScopeKey } from '@nerv-iip/business-core'
import { useQuery } from '@pinia/colada'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import {
  MES_WORK_SCOPE_REQUIRED_MESSAGE,
  MES_WORK_SCOPE_UNAVAILABLE_MESSAGE,
  bindAuthScope,
  hasScope,
  type MesScope,
  type MesSelectedWorkScope,
} from './mesShared'

export interface MesWorkScopeOption {
  label: string
  value: string
}

const MES_WORK_SCOPE_KIND_LABELS: Record<string, string> = {
  self: '本人',
  team: '班组',
  'work-center': '工作中心',
  workshop: '车间',
  organization: '组织',
}

export function mesWorkScopeKindLabel(kind: string) {
  return MES_WORK_SCOPE_KIND_LABELS[kind] ?? kind
}

// 同一 principal/org/env 的作业范围选择在整个 PDA 共享（工序执行/报工口径一致），
// 用户显式选择会记住（localStorage）——PDA 是交班轮用的手持设备，重开 App 不该丢掉刚选的范围；
// 自动兜底选择不写入，避免把兜底固化成偏好。
const MES_WORK_SCOPE_STORAGE_PREFIX = 'nerv-iip.business-pda.mes-work-scope.v1'

const sharedMesWorkScopeSelections = reactive(new Map<string, string>())

function readRememberedMesWorkScope(selectionKey: string) {
  try {
    return (
      globalThis.localStorage?.getItem(`${MES_WORK_SCOPE_STORAGE_PREFIX}:${selectionKey}`) ??
      undefined
    )
  } catch {
    return undefined
  }
}

function writeRememberedMesWorkScope(selectionKey: string, value: string) {
  try {
    globalThis.localStorage?.setItem(`${MES_WORK_SCOPE_STORAGE_PREFIX}:${selectionKey}`, value)
  } catch {
    // 持久化失败只影响「记住选择」，不影响本次会话内的共享选择。
  }
}

export function useMesPrincipalWorkScope(scope: MesScope, permissionCode: string) {
  const auth = useAuthStore()
  const principalIdentity = computed(
    () => auth.principal?.principalId?.trim() || auth.sessionId?.trim() || 'unrestored-session',
  )
  const selectionKey = computed(() =>
    [principalIdentity.value, scope.organizationId.trim(), scope.environmentId.trim()].join(':'),
  )

  // 该权限下最近一次成功响应的授权范围清单。请求参数与它相互依赖（选择来自清单、清单来自响应），
  // 必须用「最后已知值」而不是当前查询的 data：否则带选择的请求处于加载态时 data 为空，
  // 选择会被清空、查询键回退，来回震荡。principal/org/env 变化时清空重来。
  const knownAuthorizedScopes = shallowRef<MesSelectedWorkScope[]>([])
  watch(selectionKey, () => {
    knownAuthorizedScopes.value = []
  })

  // 期望选择：记住的选择仍在授权清单里就用它，否则回退清单第一项（与 useWmsWorkScope 同姿势）。
  // 只会请求「当前授权清单里存在」的范围，越权选择不可能被发出（后端仍会独立核验并 403 兜底）。
  const requestedScope = computed<MesSelectedWorkScope | undefined>(() => {
    const scopes = knownAuthorizedScopes.value
    if (scopes.length === 0) return undefined
    const storedValue =
      sharedMesWorkScopeSelections.get(selectionKey.value) ??
      readRememberedMesWorkScope(selectionKey.value)
    const stored = parseWorkScopeKey(storedValue)
    const remembered = stored
      ? scopes.find((candidate) => candidate.kind === stored.kind && candidate.id === stored.id)
      : undefined
    return remembered ?? scopes[0]
  })

  const workContextQuery = useQuery(() => {
    const requested = requestedScope.value
    const options = getBusinessConsolePrincipalWorkContextQueryOptions({
      query: {
        organizationId: scope.organizationId,
        environmentId: scope.environmentId,
        permissionCode,
        ...(requested ? { scopeKind: requested.kind, scopeId: requested.id } : {}),
      },
    })
    return {
      ...options,
      key: [...options.key, `principal:${principalIdentity.value}`],
      enabled: hasScope(scope),
    }
  })

  watch(
    () => workContextQuery.data.value,
    (envelope) => {
      if (!envelope?.success) return
      knownAuthorizedScopes.value = (envelope.data?.authorizedScopes ?? [])
        .map((candidate) => {
          const kind = candidate.kind?.trim() ?? ''
          const id = candidate.id?.trim() ?? ''
          const displayName = candidate.displayName?.trim()
          return { kind, id, ...(displayName ? { displayName } : {}) }
        })
        .filter((candidate) => candidate.kind && candidate.id)
    },
    { immediate: true },
  )

  const scopeOptions = computed<MesWorkScopeOption[]>(() =>
    knownAuthorizedScopes.value.map((candidate) => ({
      label: `${candidate.displayName || candidate.id}（${mesWorkScopeKindLabel(candidate.kind)}）`,
      value: formatWorkScopeKey(candidate.kind, candidate.id),
    })),
  )
  const scopeSelectionValue = computed<string | undefined>({
    get: () =>
      requestedScope.value
        ? formatWorkScopeKey(requestedScope.value.kind, requestedScope.value.id)
        : undefined,
    set: (value) => {
      if (!value) return
      sharedMesWorkScopeSelections.set(selectionKey.value, value)
      writeRememberedMesWorkScope(selectionKey.value, value)
    },
  })

  const selectedScope = computed<MesSelectedWorkScope | undefined>(() => {
    const envelope = workContextQuery.data.value
    if (!envelope?.success) return undefined
    const selection = envelope.data?.selectedScope
    const kind = selection?.kind?.trim()
    const id = selection?.id?.trim()
    const displayName = selection?.displayName?.trim()
    if (!kind || !id) return undefined
    return {
      kind,
      id,
      ...(displayName ? { displayName } : {}),
    }
  })
  const scopeReady = computed(() => selectedScope.value !== undefined)
  // 「读到了、但确实一个授权范围都没有」——与「还没选」是两回事，提示必须说清缺什么、去哪配。
  const scopeUnavailable = computed(
    () =>
      !workContextQuery.isLoading.value &&
      !workContextQuery.error.value &&
      workContextQuery.data.value?.success === true &&
      knownAuthorizedScopes.value.length === 0 &&
      !scopeReady.value,
  )
  const scopeMessage = computed(() => {
    if (!hasScope(scope)) return '尚未进入有效组织与环境，当前操作已禁用。'
    if (workContextQuery.isLoading.value) return '正在核验当前作业范围…'
    if (workContextQuery.error.value) return '作业范围核验失败，当前操作已禁用。请刷新后重试。'
    if (scopeReady.value) return ''
    if (scopeUnavailable.value) return MES_WORK_SCOPE_UNAVAILABLE_MESSAGE
    return MES_WORK_SCOPE_REQUIRED_MESSAGE
  })

  function requireSelectedScope() {
    const selection = selectedScope.value
    if (!selection) throw new Error(scopeMessage.value || MES_WORK_SCOPE_REQUIRED_MESSAGE)
    return selection
  }

  return {
    principalIdentity,
    requireSelectedScope,
    selectedScope,
    scopeMessage,
    scopeOptions,
    scopePending: workContextQuery.isLoading,
    scopeReady,
    scopeSelectionValue,
    scopeUnavailable,
  }
}

/**
 * 作业范围选择入口用的独立实例：work-context 查询按 key 去重，
 * 与工序执行/报工各 composable 内部的同权限实例共享同一次请求与同一份共享选择。
 */
export function useMesWorkScopeSelection(permissionCode: string) {
  return useMesPrincipalWorkScope(
    bindAuthScope(reactive({ organizationId: '', environmentId: '' })),
    permissionCode,
  )
}
