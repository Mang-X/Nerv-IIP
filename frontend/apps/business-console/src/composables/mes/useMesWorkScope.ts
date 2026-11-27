import { getBusinessConsolePrincipalWorkContextQueryOptions } from '@nerv-iip/api-client'
import { formatWorkScopeKey, parseWorkScopeKey } from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useQuery } from '@pinia/colada'
import { computed, reactive, shallowRef, watch } from 'vue'
import { businessReadState } from '../shared/businessReadState'
import {
  hasBusinessContext,
  withBusinessContextEnabled,
  type BusinessContextFields,
} from '../shared/businessContextBinding'
import {
  MES_WORK_SCOPE_REQUIRED_MESSAGE,
  MES_WORK_SCOPE_UNAVAILABLE_MESSAGE,
  defaultContext,
} from './mesShared'

export interface MesSelectedWorkScope {
  kind: string
  id: string
  displayName?: string
}

interface MesWorkOrderScopeCandidate {
  operationTasks?: Array<{
    assignedUserId?: string | null
    teamId?: string | null
    workCenterId?: string | null
  }> | null
}

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

// 同一 principal/org/env 的作业范围选择在整个 Console 共享（工单列表/详情/工序任务/排产待排池口径一致），
// 用户显式选择会记住（localStorage）；自动兜底选择不写入，避免把兜底固化成偏好。
const MES_WORK_SCOPE_STORAGE_PREFIX = 'nerv-iip.business-console.mes-work-scope.v1'

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

export function useMesPrincipalWorkScope(
  context: BusinessContextFields,
  permissionCode: string,
  canResolve = () => true,
) {
  const auth = useAuthStore()
  const principalIdentity = computed(
    () => auth.principal?.principalId?.trim() || auth.sessionId?.trim() || 'unrestored-session',
  )
  const selectionKey = computed(() =>
    [principalIdentity.value, context.organizationId.trim(), context.environmentId.trim()].join(
      ':',
    ),
  )

  // 该权限下最近一次成功响应的授权范围清单。请求参数与它相互依赖（选择来自清单、清单来自响应），
  // 必须用「最后已知值」而不是当前查询的 data：否则带选择的请求处于加载态时 data 为空，
  // 选择会被清空、查询键回退，来回震荡。principal/org/env 变化时清空重来。
  const knownAuthorizedScopes = shallowRef<MesSelectedWorkScope[]>([])
  watch(selectionKey, () => {
    knownAuthorizedScopes.value = []
  })

  // 期望选择：用户记住的选择仍在授权清单里就用它，否则回退清单第一项（与 WMS 作业范围同姿势）。
  // 只会请求「当前授权清单里存在」的范围，越权选择不可能被发出（后端仍会独立核验并 403 兜底）。
  const requestedScope = computed<MesSelectedWorkScope | undefined>(() => {
    const scopes = knownAuthorizedScopes.value
    if (scopes.length === 0) return undefined
    const storedValue =
      sharedMesWorkScopeSelections.get(selectionKey.value) ??
      readRememberedMesWorkScope(selectionKey.value)
    const stored = parseWorkScopeKey(storedValue)
    const remembered = stored
      ? scopes.find((scope) => scope.kind === stored.kind && scope.id === stored.id)
      : undefined
    return remembered ?? scopes[0]
  })

  const workContextQuery = useQuery(() => {
    const requested = requestedScope.value
    const options = withBusinessContextEnabled(
      getBusinessConsolePrincipalWorkContextQueryOptions({
        query: {
          organizationId: context.organizationId,
          environmentId: context.environmentId,
          permissionCode,
          ...(requested ? { scopeKind: requested.kind, scopeId: requested.id } : {}),
        },
      }),
      context,
    )
    const authorized = canResolve()
    return {
      ...options,
      enabled: options.enabled && authorized,
      // 未启用的接力查询不能覆盖其他入口同权限查询的缓存配置。
      key: [
        ...options.key,
        `principal:${principalIdentity.value}`,
        ...(authorized ? [] : ['disabled']),
      ],
    }
  })

  watch(
    () => workContextQuery.data.value,
    (envelope) => {
      if (!envelope?.success) return
      knownAuthorizedScopes.value = (envelope.data?.authorizedScopes ?? [])
        .map((scope) => {
          const kind = scope.kind?.trim() ?? ''
          const id = scope.id?.trim() ?? ''
          const displayName = scope.displayName?.trim()
          return { kind, id, ...(displayName ? { displayName } : {}) }
        })
        .filter((scope) => scope.kind && scope.id)
    },
    { immediate: true },
  )

  const scopeOptions = computed<MesWorkScopeOption[]>(() =>
    knownAuthorizedScopes.value.map((scope) => ({
      label: `${scope.displayName || scope.id}（${mesWorkScopeKindLabel(scope.kind)}）`,
      value: formatWorkScopeKey(scope.kind, scope.id),
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
  const scopeReadState = businessReadState(
    workContextQuery,
    () => hasBusinessContext(context) && canResolve(),
  )
  const scopePending = computed(
    () => scopeReadState.value === 'loading' || workContextQuery.isLoading.value,
  )
  const scopeReady = computed(() => selectedScope.value !== undefined)
  // 「读到了、但确实一个授权范围都没有」——与「还没选」是两回事，提示必须说清缺什么、去哪配。
  const scopeUnavailable = computed(
    () =>
      scopeReadState.value === 'ready' &&
      !scopePending.value &&
      knownAuthorizedScopes.value.length === 0 &&
      !scopeReady.value,
  )
  const scopeMessage = computed(() => {
    if (!hasBusinessContext(context)) return '尚未确定当前组织或环境，当前操作已禁用。'
    if (scopePending.value) return '正在核验当前作业范围…'
    if (scopeReadState.value === 'error') return '作业范围核验失败，当前操作已禁用。请刷新后重试。'
    if (scopeReady.value) return ''
    if (scopeUnavailable.value) return MES_WORK_SCOPE_UNAVAILABLE_MESSAGE
    return MES_WORK_SCOPE_REQUIRED_MESSAGE
  })

  function requireSelectedScope() {
    const selection = selectedScope.value
    if (!selection) throw new Error(scopeMessage.value || MES_WORK_SCOPE_REQUIRED_MESSAGE)
    return selection
  }

  function coversWorkOrder(
    candidate: MesWorkOrderScopeCandidate,
    expectedSelection: MesSelectedWorkScope,
  ) {
    // 与 Gateway PrincipalWorkScopeResolver 的范围投影及工单 Any(task) 命中语义保持一致；
    // 只使用 permission-aware work-context 返回的主体/授权子范围，不在客户端自行扩张授权。
    const envelope = workContextQuery.data.value
    const data = envelope?.success ? envelope.data : undefined
    const currentSelection = data?.selectedScope
    if (
      !currentSelection ||
      currentSelection.kind?.trim() !== expectedSelection.kind ||
      currentSelection.id?.trim() !== expectedSelection.id
    ) {
      return false
    }

    const tasks = candidate.operationTasks ?? []
    switch (expectedSelection.kind) {
      case 'organization':
        return expectedSelection.id === context.organizationId.trim()
      case 'self': {
        const principalId = data?.principal?.id?.trim()
        return (
          !!principalId &&
          principalId === expectedSelection.id &&
          tasks.some((task) => task.assignedUserId?.trim() === principalId)
        )
      }
      case 'team':
        return tasks.some((task) => task.teamId?.trim() === expectedSelection.id)
      case 'work-center':
        return tasks.some((task) => task.workCenterId?.trim() === expectedSelection.id)
      case 'workshop': {
        const authorizedWorkCenterIds = new Set(
          (data?.authorizedScopes ?? [])
            .filter((scope) => scope.kind?.trim() === 'work-center')
            .map((scope) => scope.id?.trim())
            .filter((id): id is string => !!id),
        )
        const coveredWorkCenterIds = new Set(
          (data?.coveredWorkCenters ?? [])
            .filter(
              (workCenter) =>
                workCenter.workshopId?.trim() === expectedSelection.id &&
                authorizedWorkCenterIds.has(workCenter.id?.trim() ?? ''),
            )
            .map((workCenter) => workCenter.id?.trim())
            .filter((id): id is string => !!id),
        )
        return tasks.some((task) => coveredWorkCenterIds.has(task.workCenterId?.trim() ?? ''))
      }
      default:
        return false
    }
  }

  return {
    coversWorkOrder,
    principalIdentity,
    refreshScope: () => workContextQuery.refetch(),
    requireSelectedScope,
    selectedScope,
    scopeMessage,
    scopeError: workContextQuery.error,
    scopeOptions,
    scopePending,
    scopeReadState,
    scopeReady,
    scopeSelectionValue,
    scopeUnavailable,
  }
}

/**
 * 作业范围选择入口用的独立实例：work-context 查询按 key 去重，
 * 与各列表/详情 composable 内部的同权限实例共享同一次请求与同一份共享选择。
 */
export function useMesWorkScopeSelection(permissionCode: string) {
  return useMesPrincipalWorkScope(defaultContext(), permissionCode)
}
