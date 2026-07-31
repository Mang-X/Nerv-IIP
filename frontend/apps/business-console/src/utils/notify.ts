/**
 * 统一的操作反馈（通知）工具。
 *
 * 规则见 `frontend/DESIGN/patterns/feedback-and-notifications.md`：
 * - 操作结果（成功/失败，含网络/服务器错误）一律用 toast，**不**在页面或弹窗里留常驻文字。
 * - 字段级校验才用内联（红框 + 汇总），不走这里。
 */
import { toast } from '@nerv-iip/ui'

/** 从各种 error 形态里取出原始文本。 */
function rawMessage(error: unknown): string {
  if (error instanceof Error) return error.message
  if (typeof error === 'string') return error
  if (error && typeof error === 'object' && 'message' in error) {
    return String((error as { message: unknown }).message ?? '')
  }
  return ''
}

/**
 * 把后端/网络错误转成对用户友好的中文。
 * 绝不把 `downstream-invalid-response` / `502` 这类开发术语甩给用户。
 */
export function friendlyErrorMessage(error: unknown, fallback = '操作失败，请稍后重试。'): string {
  const raw = rawMessage(error)
  if (!raw) return fallback
  if (
    /business-operation-unconfirmed|BusinessOperationUnconfirmedError|权威状态尚未确认|写操作回执不完整|写操作缺少权威回执/i.test(
      raw,
    )
  ) {
    return '操作结果尚未确认，请保留当前操作并刷新列表核实；确认未生效后再重试。'
  }
  // 网关超时（#1306）：任务可能仍在下游处理，引导用户去列表核实而不是盲目重试。
  // 必须排在通用网络分支之前，否则 `downstream-timeout` 会被 /timeout/ 吞成泛化文案。
  if (/downstream-timeout|\b504\b|gateway ?time-?out/i.test(raw)) {
    return '服务响应超时，任务可能仍在处理；请稍后刷新相关列表查看结果，勿立即重复提交。'
  }
  if (
    /downstream-invalid-response|\b502\b|bad ?gateway|\b503\b|service unavailable|\b500\b/i.test(
      raw,
    )
  ) {
    return '服务暂时不可用，操作结果可能尚未确认；请刷新列表核实后再重试。'
  }
  if (/failed to fetch|networkerror|network error|timeout|timed out|econn/i.test(raw)) {
    return '网络异常，操作结果可能尚未确认；请刷新列表核实后再重试。'
  }
  if (/\b401\b|unauthor/i.test(raw)) return '登录已过期，请重新登录。'
  if (/\b403\b|forbidden|permission/i.test(raw)) return '没有权限执行此操作。'
  if (/\b404\b|not found|does not exist|scope mismatch/i.test(raw)) {
    return '操作对象不存在或已不在当前业务范围，请刷新列表后重试。'
  }
  if (
    /(?:code|编码|名称|name).{0,24}(?:already exists|duplicat|已存在|已被占用)|(?:already exists|duplicat).{0,24}(?:code|编码|名称|name)/i.test(
      raw,
    )
  ) {
    return '编码或名称已存在，请更换后重试。'
  }
  if (/\b409\b|conflict|idempotency|intent|lifecycle|already bound/i.test(raw)) {
    return '当前状态或操作意图发生冲突，请刷新列表并核实最新状态后再处理。'
  }
  if (/\b422\b|unprocessable|validation|invalid request/i.test(raw)) {
    return '提交内容未通过校验，请检查填写项后重试。'
  }
  if (/system-managed|cannot be updated/i.test(raw)) return '该项由系统管理（平台固化），不可修改。'
  // 后端返回的可读中文业务校验信息（短文本）直接透传。
  if (/[一-龥]/.test(raw) && raw.length <= SERVER_MESSAGE_MAX_LENGTH) return raw
  return fallback
}

/**
 * 服务端消息的最大透传长度：超长的堆栈/序列化体不往界面上甩。
 * 与 `friendlyErrorMessage` 里中文透传的阈值是**同一个数**，避免两处漂移。
 */
const SERVER_MESSAGE_MAX_LENGTH = 60
/** 按优先级读取的消息字段：信封 message → RFC7807 detail → problem title。 */
const SERVER_MESSAGE_FIELDS = ['message', 'detail', 'title'] as const
/** 消息可能藏在下一层的容器字段里（拦截器包装 / 嵌套信封）。 */
const SERVER_MESSAGE_CONTAINERS = ['error', 'data', 'body', 'response', 'cause'] as const

/**
 * 取出**服务端真正说的那句话**：信封 `message`、RFC7807 `detail`/`title`、校验错误汇总。
 *
 * 为什么必须有它：generated client 在 `throwOnError` 下抛出的是**解析后的响应体**（普通对象），
 * 并不是 `Error` 实例。只判 `error instanceof Error` 会把所有 HTTP 失败都吞成猜测性兜底文案，
 * 用户看不到后端到底报了什么（MAN-691 / #1259）。
 *
 * 取不到就返回空串，由调用方决定兜底文案。
 */
export function serverErrorMessage(error: unknown): string {
  return readServerMessage(error, new Set<object>(), 0)
}

function readServerMessage(error: unknown, seen: Set<object>, depth: number): string {
  if (error == null || depth > 4) return ''
  if (typeof error === 'string') return clampServerMessage(error)
  if (typeof error !== 'object') return ''
  // 循环引用（拦截器把 response 挂回 error 上很常见）不能把递归拖进死循环。
  if (seen.has(error)) return ''
  seen.add(error)

  const record = error as Record<string, unknown>
  for (const field of SERVER_MESSAGE_FIELDS) {
    const value = record[field]
    if (typeof value === 'string' && value.trim()) return clampServerMessage(value)
  }

  const validation = readValidationErrors(record.errors)
  if (validation) return validation

  for (const container of SERVER_MESSAGE_CONTAINERS) {
    const nested = readServerMessage(record[container], seen, depth + 1)
    if (nested) return nested
  }
  return ''
}

/** RFC7807 `errors`：既可能是 `{ 字段: [消息] }`，也可能是消息数组。 */
function readValidationErrors(errors: unknown): string {
  const messages: string[] = []
  const collect = (value: unknown) => {
    if (typeof value === 'string' && value.trim()) messages.push(value.trim())
    else if (Array.isArray(value)) value.forEach(collect)
  }

  if (Array.isArray(errors)) collect(errors)
  else if (errors && typeof errors === 'object') Object.values(errors).forEach(collect)

  return messages.length > 0 ? clampServerMessage(messages.join('；')) : ''
}

function clampServerMessage(raw: string): string {
  const text = raw.trim()
  if (!text) return ''
  return text.length > SERVER_MESSAGE_MAX_LENGTH
    ? `${text.slice(0, SERVER_MESSAGE_MAX_LENGTH - 1)}…`
    : text
}

/** 状态码可能藏在这些字段上：拦截器挂的 `response`、RFC7807 的 `status`、包装后的 `statusCode`。 */
const STATUS_FIELDS = ['status', 'statusCode'] as const

/**
 * 取出这次失败的 **HTTP 状态码**。
 *
 * 为什么必须有它：generated client 在 `throwOnError` 下抛的是**解析后的响应体对象**，
 * 不是 `Error` 实例——靠 `error instanceof Error && error.message.includes('403')` 判权限
 * 永远不成立，真实 403 会退化成普通失败态（MAN-698 台账 / #1298 规格轴）。
 * 本仓库的 error 拦截器（`@nerv-iip/api-client` 的 `configureApiClient`）会把原始
 * `Response` 以非枚举属性挂到 error 上，所以 `error.response.status` 一定拿得到。
 *
 * 取不到返回 `undefined`——调用方据此走「未知失败」，不要猜。
 */
export function errorStatusCode(error: unknown): number | undefined {
  return readStatusCode(error, new Set<object>(), 0)
}

/**
 * `status` 是个很常见的**业务字段名**（工单状态、任务状态…），响应体里出现数值 `status`
 * 完全可能与 HTTP 无关。只认合法 HTTP 状态码区间，避免把领域状态误当成 HTTP 码
 * ——否则一个 `status: 403` 语义的业务枚举就能把页面骗进「无权限」空态。
 */
function isHttpStatusCode(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 100 && value <= 599
}

function readStatusCode(error: unknown, seen: Set<object>, depth: number): number | undefined {
  if (error == null || typeof error !== 'object' || depth > 4) return undefined
  if (seen.has(error)) return undefined
  seen.add(error)

  const record = error as Record<string, unknown>
  for (const field of STATUS_FIELDS) {
    const value = record[field]
    if (isHttpStatusCode(value)) return value
  }

  for (const container of ['response', 'error', 'data', 'body', 'cause'] as const) {
    const nested = readStatusCode(record[container], seen, depth + 1)
    if (nested !== undefined) return nested
  }
  return undefined
}

/**
 * 这次失败是不是「没有权限」（403）？——按状态码判，取不到状态码才退回消息文本匹配
 * （少数场景 error 只是一个带 403 字样的字符串/Error）。
 * 页面据此渲染「无权限」空态，而不是把 403 当成普通加载失败。
 */
export function isForbiddenError(error: unknown): boolean {
  const status = errorStatusCode(error)
  if (status !== undefined) return status === 403
  // 兜底只认**技术串**（`403 Forbidden` 这类）。中文领域消息里出现的 403 是业务数字
  // （「任务状态为 403 号工序」），不是状态码——那种情况下宁可不认，把领域消息原样上屏，
  // 也好过把一次普通失败误判成「无权限」。
  const raw = `${serverErrorMessage(error)} ${rawMessage(error)}`
  if (/[一-龥]/.test(raw)) return false
  return /\b403\b|forbidden/i.test(raw)
}

/**
 * 写操作失败的统一反馈：**分层透传**，一句话——「服务端说的人话上屏，通用 HTTP 文案先映射」。
 *
 * 1. **服务端领域消息**（中文、可行动，如「工单缺少生产版本，无法排程」）→ 带动作前缀原样上屏；
 * 2. **通用 HTTP / 英文 problem 文案**（`Internal Server Error`、`502`、`Failed to fetch` …）
 *    → 交给 `friendlyErrorMessage` 映射成人话，**原文只进 `console.error`**：
 *    反馈规范禁止英文错误码 / 5xx 原文上屏（`frontend/DESIGN/patterns/feedback-and-notifications.md`）；
 * 3. 服务端什么都没说 → 用调用方给的领域兜底文案。
 *
 * 之所以要它而不是直接 `notifyError`：写操作要让用户知道**是哪个动作**失败了
 * （「发布失败：…」/「生成失败：…」），且服务端的领域拒绝理由必须原样看得见（MAN-691 / #1259）。
 */
export function notifyOperationFailure(action: string, error: unknown, fallback: string): void {
  const raw = serverErrorMessage(error)
  const message = friendlyErrorMessage(raw || error, '')
  if (raw && message !== raw) {
    // 没上屏的原文留给排障：控制台能看到后端到底说了什么。
    console.error(`[${action}] 服务端原始错误：`, raw, error)
  }
  toast.error(message ? `${action}：${message}` : fallback)
}

/**
 * **行内错误态**（列表加载失败条、弹窗内 `submitError` 之类）的统一文案。
 *
 * 与 toast 走**同一条分层透传链**，避免「toast 说人话、行内条却是 `Internal Server Error`」的
 * 两套口径：中文领域消息原样显示，英文 HTTP / 5xx 文案映射成人话（原文进 `console.error`）。
 *
 * 无错误时返回空串，模板可直接 `v-if` 判空。
 */
export function inlineErrorMessage(error: unknown, fallback = '请求失败，请稍后重试。'): string {
  if (!error) return ''
  const raw = serverErrorMessage(error)
  const message = friendlyErrorMessage(raw || error, fallback)
  if (raw && message !== raw) {
    console.error('[加载失败] 服务端原始错误：', raw, error)
  }
  return message
}

/** 成功反馈。 */
export function notifySuccess(message: string): void {
  toast.success(message)
}

/**
 * 提醒反馈：请求**成功了**，但业务结果不是用户想要的那一档
 * （如「采购申请转单成功返回，但缺少有效价源」）——既不是失败也不该报喜。
 *
 * 参数是**调用方写死的中文文案**，不接 error：它不属于分层透传链，也不需要映射。
 * 存在的理由是保住「业务页不直接调 toast」这条边界（否则同类提醒会绕过 notify 各写一套）。
 */
export function notifyWarning(message: string): void {
  toast.warning(message)
}

/**
 * 失败反馈：toast.error（友好文案），不在页面留常驻错误条。
 *
 * 与 `notifyOperationFailure` **同一条分层透传链**（只是不带动作前缀）：先取服务端真正说的那句话
 * （信封 `message` → RFC7807 `detail`/`title` → 字段校验 `errors`），中文领域消息原样上屏，
 * 英文 HTTP / 5xx 文案走 `friendlyErrorMessage` 映射、原文只进 `console.error`。
 *
 * 为什么不能只写 `friendlyErrorMessage(error, fallback)`：generated client 在 `throwOnError`
 * 下抛的是**解析后的响应体对象**，`rawMessage` 只认 `Error`/字符串/顶层 `message`，
 * 于是 `{ detail: '报价单已过期，不能转订单' }` 这类 400 全被吞成兜底文案（MAN-700 / #1289）。
 */
export function notifyError(error: unknown, fallback?: string): void {
  const raw = serverErrorMessage(error)
  const message = friendlyErrorMessage(raw || error, fallback)
  if (raw && message !== raw) {
    console.error('[操作失败] 服务端原始错误：', raw, error)
  }
  toast.error(message)
}
