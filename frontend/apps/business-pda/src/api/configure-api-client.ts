import { configureApiClient, type ConfigureApiClientOptions } from '@nerv-iip/api-client'
import { resolveGatewayBaseUrl } from './gateway-base-url'
import { createTimeoutFetch, resolveRequestTimeoutMs } from './request-timeout'
import { setUnauthorizedHandler } from './unauthorized'

/**
 * PDA 的 api-client 唯一注入点（AGENTS 硬性规则 3）。
 *
 * 从 `main.ts` 提出来是为了让**接线本身可测**：`onUnauthorized` 有没有被转交给手搓字节面
 * 那条支路，用源码文本扫是扫不出「接没接上」的——换个写法就绕过去了。这里做三件事：
 *  1. 注入 R4 基址（Web/dev 留空走 vite 代理，APK 必须给绝对值）；
 *  2. 注入全局 30s 超时 + 离线预检 fetch；
 *  3. 把 `onUnauthorized` 登记给 `./unauthorized`，供 tus 手搓两跳在 401 时走同一套兜底。
 */
export function configurePdaApiClient(options: ConfigureApiClientOptions): void {
  setUnauthorizedHandler(options.onUnauthorized)
  configureApiClient({
    ...options,
    baseUrl: resolveGatewayBaseUrl(),
    fetch: createTimeoutFetch({ timeoutMs: resolveRequestTimeoutMs() }),
  })
}
