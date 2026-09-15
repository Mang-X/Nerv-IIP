import {
  listBusinessConsoleSearchableDirectoryQueryOptions,
  type BusinessConsoleSearchableDirectoryEnvelope,
} from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useQuery } from '@pinia/colada'
import { computed, ref } from 'vue'

const PAGE_SIZE = 100

/**
 * 一条可选停机原因。`code` 是 **写面唯一权威值**：直接来自目录条目，既不 trim 也不改大小写。
 * Maintenance v2 (`assetUnavailableReasonCode`) 按 organization + environment 精确命中校验，
 * 前端任何"归一化"都可能把一个码改写成另一个合法码，或把合法码改成被拒的近似值。
 */
export interface DowntimeReasonOption {
  /** 提交给 v2 的原值。 */
  code: string
  /** 纯名称（目录没给名称时回落成码，不编名字）。 */
  name: string
  /** 列表展示口径「名称（码）」。 */
  label: string
}

/** 目录读不出来时的分类；`ok` 之外一律**不可选**，且绝不回退自由文本或伪默认码。 */
export type DowntimeReasonDirectoryState =
  | 'scope-pending'
  | 'loading'
  | 'forbidden'
  | 'failed'
  | 'unavailable'
  | 'empty'
  | 'ok'

function isForbidden(error: unknown): boolean {
  if (!error || typeof error !== 'object') return false
  const value = error as { status?: unknown; response?: { status?: unknown } }
  return value.status === 403 || value.response?.status === 403
}

/**
 * PDA 维修报修的停机原因目录（Maintenance 权威 `downtime-reason` 词表）。
 *
 * - 目录由 BusinessGateway 按请求 organization/environment 过滤并按 principal 授权范围收敛，
 *   前端**不再自造过滤**：别的租户/环境的码根本不会出现在响应里。
 * - 不加 principal 权限码前置（与 business-console 同一裁定 #2793）：权威是网关，
 *   403 是可归因的权威事实，前端权限码滞后时加前置只会让页面永远说不清原因。
 * - 读失败/词表不可用/组织没配 —— 三种都只产生**明确错误态**，调用方拿不到任何可选项，
 *   因此不可能提交伪造原因码；用户仍可走"不登记设备不可用"的 null 路径。
 */
export function useMaintenanceDowntimeReasonDirectory() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const scopeReady = computed(() => Boolean(organizationId.value && environmentId.value))
  const keyword = ref('')

  const directoryQuery = useQuery(() => {
    const trimmedKeyword = keyword.value.trim()
    return {
      ...listBusinessConsoleSearchableDirectoryQueryOptions({
        path: { directoryType: 'downtime-reason' },
        query: {
          organizationId: organizationId.value,
          environmentId: environmentId.value,
          pageIndex: 1,
          pageSize: PAGE_SIZE,
          rankingMode: 'default',
          ...(trimmedKeyword ? { keyword: trimmedKeyword } : {}),
        },
      }),
      enabled: scopeReady.value,
    }
  })

  const envelope = computed(
    () => directoryQuery.data.value as BusinessConsoleSearchableDirectoryEnvelope | undefined,
  )
  /**
   * **当前链路不可达的防御分支。** `BusinessConsoleSearchableDirectoryEndpoint.QueryMaintenanceAsync`
   * 调 `FromItems` 时只传了 `authorityDirectoryType:`，没传 `authorityConfigured`，
   * 该参数默认 `true`（`BusinessConsoleModels.cs:112`），所以 downtime-reason 目前恒
   * `status: "available"`；只有 `priority` 目录会走出 `unavailable`。
   * 这里仍然 fail closed 地识别它：网关哪天补上探针，前端不会把"没配词表"当成空目录。
   */
  const directoryUnavailable = computed(
    () => envelope.value?.success === true && envelope.value.data?.status === 'unavailable',
  )

  const reasonOptions = computed<DowntimeReasonOption[]>(() => {
    if (envelope.value?.success !== true || directoryUnavailable.value) return []
    return (envelope.value.data?.items ?? [])
      .map((item) => {
        // 只跳过"没有码"的条目；**留下的码原样承载**，不 trim、不改大小写。
        const code = item.code
        if (!code || code.trim().length === 0) return undefined
        const name = item.displayName?.trim()
        return { code, name: name || code, label: name ? `${name}（${code}）` : code }
      })
      .filter((option): option is DowntimeReasonOption => option !== undefined)
  })

  const state = computed<DowntimeReasonDirectoryState>(() => {
    if (!scopeReady.value) return 'scope-pending'
    const error = directoryQuery.error.value
    if (error) return isForbidden(error) ? 'forbidden' : 'failed'
    // 尚无任何响应 = 还在读；HTTP 200 但 `success:false` 是**读失败**，不是空目录
    // （当成空目录会把一次故障说成"组织尚未配置"，把人指去配词表）。
    if (envelope.value === undefined) return 'loading'
    if (envelope.value.success !== true) return 'failed'
    if (directoryUnavailable.value) return 'unavailable'
    return reasonOptions.value.length > 0 ? 'ok' : 'empty'
  })

  /**
   * 每种非 `ok` 状态说一句**能指出下一步的话**：笼统的"读取失败"会让操作工一直刷新，
   * 说成"尚未配置"又会把没权限的人指去配词表。
   */
  const stateMessage = computed(() => {
    switch (state.value) {
      case 'scope-pending':
        return '登录范围尚未就绪，暂不能读取停机原因'
      case 'loading':
        return '正在读取停机原因…'
      case 'forbidden':
        return '当前账号没有停机原因词表的读取权限，请联系管理员开通'
      case 'failed':
        return '停机原因读取失败，请重试'
      case 'unavailable':
        // 语义是"权威服务没配这份词表"（producer 的 `directory-authority-unconfigured`），
        // 是配置事实而非瞬时故障——写"请稍后重试"会让人白等。
        return '权威服务尚未配置停机原因词表，请联系管理员配置'
      case 'empty':
        return keyword.value.trim() ? '没有匹配的停机原因' : '当前组织尚未配置可用停机原因'
      default:
        return ''
    }
  })

  /** 目录不可用时**唯一**允许的后果：不能选原因，只能提交不登记设备停机的报修。 */
  const canSelectReason = computed(() => state.value === 'ok')

  const reasonsTotal = computed(() => {
    if (envelope.value?.success !== true || directoryUnavailable.value) return 0
    return envelope.value.data?.total ?? 0
  })
  /**
   * 一次只取一页且不翻页，所以超量组织必然被截断。**必须让工人知道**：
   * 否则"翻不到的码"和"本组织没有这个码"在界面上长得一模一样，人就会改选
   * "不登记设备不可用"——本该记录的停机原因就这样丢了。
   */
  const reasonsTruncated = computed(
    () => state.value === 'ok' && reasonsTotal.value > reasonOptions.value.length,
  )

  function search(value: string) {
    keyword.value = value
  }

  return {
    reasonOptions,
    reasonsTotal,
    reasonsTruncated,
    reasonsError: directoryQuery.error,
    reasonsPending: directoryQuery.isLoading,
    reasonKeyword: keyword,
    scopeReady,
    state,
    stateMessage,
    canSelectReason,
    search,
    refreshReasons: () =>
      scopeReady.value ? directoryQuery.refetch() : Promise.resolve(undefined),
  }
}
