import { computed, type ComputedRef } from 'vue'

/**
 * 业务读面的四态。
 *
 * 存在的理由（真实事故）：列表/概览的计数一律写成 `envelopeTotal(query.data.value)`，
 * 而信封为 `undefined`（请求在途 / 请求失败 / 业务上下文未就绪导致查询根本没发）时它返回 **0**。
 * 于是页面在「取不到数据」时渲染出「0 条阻塞 + 一切正常」——系统在没有依据的情况下
 * 向车间管理者**断言现场安全**。这比少显示信息严重得多。
 *
 * 四态把三种「没数字」的原因和一种「真的是 0」彻底分开：
 * - `idle`：业务上下文（组织 / 环境）未就绪，查询被禁用，压根没请求过 → 只能提示去选上下文
 * - `loading`：请求在途 → 只能说「正在读取」
 * - `error`：请求失败，或 HTTP 200 但信封 `success:false` → 必须明说「取不到，无法判断」
 * - `ready`：确实拿到了数据。**只有此时** 0 才等于「真的没有」，也只有此时才允许下结论
 */
export type BusinessReadState = 'idle' | 'loading' | 'error' | 'ready'

/** 读面数字取不到时的统一占位。绝不用 0 冒充「取到了且为零」。 */
export const UNAVAILABLE_VALUE = '—'

/**
 * 只依赖 `{ value }` 结构，避免和 `Ref` / `ShallowRef` / 只读 ref 的具体形态耦合，
 * 直接接 `useQuery(...)` 返回值即可。
 */
interface ReadQueryLike<TEnvelope> {
  data: { value: TEnvelope | undefined }
  error: { value: unknown }
  isLoading: { value: boolean }
}

/**
 * 由查询自身 + 「查询是否被启用」推导读面四态。
 *
 * 注意两处刻意的顺序：
 * 1. `error` 优先于既有数据——失败后即使缓存里还留着上一轮结果，也不允许拿旧数据去断言现场状态。
 * 2. 已有成功数据时的后台重取仍算 `ready`，避免每次刷新整屏闪成「—」；加载指示交给 `pending`。
 */
export function businessReadState<TEnvelope extends { success?: boolean }>(
  query: ReadQueryLike<TEnvelope>,
  isEnabled: () => boolean,
): ComputedRef<BusinessReadState> {
  return computed(() => {
    if (!isEnabled()) {
      return 'idle'
    }
    if (query.error.value != null) {
      return 'error'
    }
    const envelope = query.data.value
    if (envelope === undefined) {
      return 'loading'
    }
    // HTTP 200 + success:false 同样是「没拿到」，不能当成空结果放行。
    return envelope.success === true ? 'ready' : 'error'
  })
}

/** 读面数字的显示值：只有 `ready` 给真数字，其余一律 `—`。 */
export function readStateValue(
  state: BusinessReadState,
  value: number | null | undefined,
): string | number {
  return state === 'ready' && value != null ? value : UNAVAILABLE_VALUE
}

/** 非 `ready` 时给数字配一句「为什么没有数字」的短说明；`ready` 返回空串。 */
export function readStateNote(state: BusinessReadState): string {
  if (state === 'idle') return '未选择业务范围'
  if (state === 'loading') return '正在读取'
  if (state === 'error') return '数据获取失败'
  return ''
}
