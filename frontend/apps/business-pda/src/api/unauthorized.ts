/**
 * 把**非 api-client 支路**上的 401 接回应用唯一的失效会话兜底。
 *
 * 背景：交接班附件的 tus `HEAD` / `PATCH` 进不了 OpenAPI 契约，只能手搓 fetch
 * （见 `./shift-handover-upload`）。但「手搓传输」不等于「手搓一整套认证与失败处置」——
 * 走 api-client 的请求在 401 时会命中 `configureApiClient({ onUnauthorized })`（清会话 + 跳登录），
 * 手搓那两跳如果各自弹个提示了事，用户就会卡在一个已经失效的会话里反复重试。
 *
 * 这里**不新建一套**：`main.ts` 在唯一的 `configureApiClient` 注入点上把 `options.onUnauthorized`
 * 原样登记进来，手搓支路只是调用同一个函数。改兜底行为仍然只有那一个地方要改。
 */
let unauthorizedHandler: (() => void) | undefined

/** 由 `main.ts` 在 `configureApiClient` 包装里登记；传 `undefined` 可解绑（测试用）。 */
export function setUnauthorizedHandler(handler?: () => void): void {
  unauthorizedHandler = handler
}

/**
 * 通知应用当前会话已失效。
 *
 * 未登记处理器时静默返回：那只发生在 `main.ts` 之外的宿主（单测直接挂载组件），
 * 此时抛错只会把被测逻辑淹掉，而真实缺口由 `main.ts` 的接线用例守住。
 */
export function notifyUnauthorized(): void {
  unauthorizedHandler?.()
}
