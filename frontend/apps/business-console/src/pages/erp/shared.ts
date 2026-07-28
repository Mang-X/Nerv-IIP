import type { ComputedRef } from 'vue'

/** 取不到数时的统一占位。财务页尤其不许拿 0 顶上——「¥0.00 余额」是会被当真的。 */
export const UNAVAILABLE_TEXT = '—'

/**
 * 金额格式化。
 *
 * 曾踩坑：这里原本写的是 `value ?? 0`，于是 `/erp/finance` 在摘要接口失败或尚未返回时
 * 把应收 / 应付 / 待入账成本全部渲染成 **¥0.00**——性质等同于「谎称现场无阻塞」。
 * 现在缺值一律显 `—`；真实的 0 仍然照常显示 ¥0.00。
 */
export function formatAmount(value?: number | null, currency = 'CNY') {
  if (value === null || value === undefined || Number.isNaN(value)) return UNAVAILABLE_TEXT
  return new Intl.NumberFormat('zh-CN', {
    style: 'currency',
    currency,
    maximumFractionDigits: 2,
  }).format(value)
}

export function formatDate(value?: string | null) {
  if (!value) return '-'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '-' : d.toLocaleDateString('zh-CN')
}

export function formatDateTime(value?: string | null) {
  if (!value) return '-'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '-' : d.toLocaleString('zh-CN')
}

/** 数量格式化。与金额同理：缺值显 `—`，不拿 0 冒充「取到了且为零」。 */
export function formatQuantity(value?: number | null) {
  if (value === null || value === undefined || Number.isNaN(value)) return UNAVAILABLE_TEXT
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(value)
}

export function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}

export function firstQueryParam(value: unknown) {
  if (Array.isArray(value)) return value[0] ? String(value[0]) : undefined
  return value ? String(value) : undefined
}

/**
 * 选择器（NvEntityPicker / EntityMultiPicker）的校验红框：这些控件把触发按钮包在一层
 * 容器里，`data-invalid` 只有 NvInput 认，所以统一用同一条边框类描红，视觉与输入框一致。
 * 传入 undefined 而不是空串，避免在未出错时留下空 class 属性。
 */
export function pickerInvalidClass(invalid: boolean) {
  return invalid ? '[&>button]:border-destructive' : undefined
}

export function unwrapRef<T>(value: T | ComputedRef<T>): T {
  return typeof value === 'object' && value !== null && 'value' in value ? value.value : value
}

/* ────────────────────────── ERP 读面六档状态 ────────────────────────── */

/**
 * ERP 13 张单据页共用的读面状态。
 *
 * 存在的理由（真实事故）：页面一律写 `:count="`${total} 张`"` + `empty-message="未找到…"`，
 * 而 `total` 在**上下文未就绪 / 请求在途 / 请求失败**三种情况下都是 0。于是「接口挂了」
 * 和「今天真的没有单据」渲染成同一句「0 张 · 未找到」——把系统故障伪装成业务清爽。
 *
 * 六档把三种「没数字」的原因、两种「真没有」的原因和一种「有数据」彻底拆开：
 * - `unscoped`   业务上下文未就绪，查询 `enabled:false`，压根没发过请求
 * - `loading`    请求在途，还不知道结果
 * - `failed`     请求失败 / 信封 `success:false`，**只能说取不到，不能下结论**
 * - `filtered`   查到了，但当前筛选条件下没有匹配（改条件就可能有）
 * - `empty`      查到了，确实一条都没有（这才允许说"还没有"）
 * - `ready`      查到了且有数据
 *
 * 注意 `unscoped` 必须由调用方显式给出：pinia-colada 在 `enabled:false` 时
 * `asyncStatus` 停在 `idle`，`isLoading` 为 **false**，只看 pending/error 会把
 * 「压根没查」当成「查过了、是 0」。
 */
export type ErpReadStateKind = 'unscoped' | 'loading' | 'failed' | 'filtered' | 'empty' | 'ready'

export interface ErpReadStateInput {
  /** 单据中文名，如「采购申请」。所有文案由它生成，页面不再逐句手写。 */
  noun: string
  /** 计数量词，如「张」「条」「笔」。 */
  unit: string
  /** 业务上下文（组织 / 环境）是否已就绪。 */
  ready: boolean
  /** 请求是否在途。 */
  pending: boolean
  /** 请求错误对象；非空即失败。 */
  error: unknown
  /** 服务端返回的总数。 */
  total: number
  /** 当前是否有生效的筛选条件（关键字 / 状态 / 单号等）。 */
  filtered?: boolean
  /** 真 0 条时的一句话引导：这类单据从哪里来。 */
  emptyHint: string
}

export interface ErpReadState {
  kind: ErpReadStateKind
  /** 读数是否可信。为 false 时页头计数、金额、KPI 一律显 `—`，不显 0。 */
  trustworthy: boolean
  /** `NvPageHeader` 的 `:count`；加载中给 `undefined`，不出假读数。 */
  count: string | undefined
  /** `NvDataTable` 的 `empty-message`。 */
  emptyMessage: string
  /** `NvDataTable` 的 `:error`（失败时透传原始错误，供组件取 message）。 */
  error: unknown
  /** `NvDataTable` 的 `:error-message`。 */
  errorMessage: string | undefined
  /** `NvDataTable` 的 `:awaiting-scope`。 */
  awaitingScope: boolean
  /** `NvDataTable` 的 `:awaiting-scope-message`。 */
  awaitingScopeMessage: string
}

const UNSCOPED_MESSAGE = '尚未选择组织与环境，还没有发起查询——请先在顶部选择业务范围。'

export function erpReadState(input: ErpReadStateInput): ErpReadState {
  const { noun, unit, ready, pending, error, total, filtered = false, emptyHint } = input

  const base = {
    error: undefined as unknown,
    errorMessage: undefined as string | undefined,
    awaitingScope: false,
    awaitingScopeMessage: UNSCOPED_MESSAGE,
  }

  // 只有「没就绪 **且** 手上确实没有结果」才算未查询；已经拿到行的情况一律按实际数据走，
  // 免得上下文短暂为空时把真实结果抹成「还没查」。
  if (!ready && total === 0 && error == null) {
    return {
      ...base,
      kind: 'unscoped',
      trustworthy: false,
      count: UNAVAILABLE_TEXT,
      emptyMessage: UNSCOPED_MESSAGE,
      awaitingScope: true,
    }
  }

  // 失败优先于在途：重试期间仍然停在失败态，不许闪回「0 条」。
  if (error != null) {
    return {
      ...base,
      kind: 'failed',
      trustworthy: false,
      count: `${noun}读取失败`,
      emptyMessage: `没有取到${noun}数据，无法判断当前是否有单据。`,
      error,
      errorMessage: `没有取到${noun}数据，当前无法判断是否有待处理的单据。请重试，或稍后再看。`,
    }
  }

  if (pending && total === 0) {
    return {
      ...base,
      kind: 'loading',
      trustworthy: false,
      count: undefined,
      emptyMessage: `正在读取${noun}…`,
    }
  }

  if (total > 0) {
    return {
      ...base,
      kind: 'ready',
      trustworthy: true,
      count: `${total} ${unit}${noun}`,
      emptyMessage: emptyHint,
    }
  }

  if (filtered) {
    return {
      ...base,
      kind: 'filtered',
      trustworthy: true,
      count: `当前筛选下 0 ${unit}${noun}`,
      emptyMessage: `当前筛选条件下没有匹配的${noun}，清空关键字或状态后再查一次。`,
    }
  }

  return {
    ...base,
    kind: 'empty',
    trustworthy: true,
    count: `0 ${unit}${noun}`,
    emptyMessage: emptyHint,
  }
}

/** 读数取不到时统一显 `—`；只有 `trustworthy` 才给真数字。 */
export function readCount(state: ErpReadState, value: number | null | undefined) {
  return state.trustworthy && value != null ? value : UNAVAILABLE_TEXT
}
