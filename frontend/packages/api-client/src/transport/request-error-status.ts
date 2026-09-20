/**
 * 从一个被拒的请求错误里读出 HTTP 状态判定（框架无关，纯 TS）。
 *
 * **为什么住在 transport 层**：`error.response` 这个形状不是业务约定，是本层
 * `client-config.ts` 的 error 拦截器亲手挂上去的——生成的客户端抛出的错误对象本身没有
 * `response`，是那段 `Object.defineProperty(candidate, 'response', …)` 补上的。判据跟着
 * 造出形状的那一层走，调用方就不必各自复刻「`status` 还是 `response.status`」这条知识。
 *
 * 收拢前 business-pda 的 `composables/` 下有**三份逐字相同**的实现
 * （`usePdaBarcodeResolver` / `useMaintenanceDowntimeReasonDirectory` / `useWorkbenchHome`），
 * 其中一份已经导出、另两份仍是私有副本。
 */

/**
 * 是否是 403。
 *
 * 两处都要看：抛出体自己带 `status`（部分手搓 fetch 路径），或由本层拦截器挂上的
 * `response.status`。只认其一会让另一条链路上的 403 被降级成「未知错误」。
 */
export function isForbiddenRequestError(error: unknown): boolean {
  if (!error || typeof error !== 'object') return false
  const value = error as { status?: number; response?: { status?: number } }
  return value.status === 403 || value.response?.status === 403
}
