import type { Page } from '@playwright/test'

/**
 * CDP 网络节流适配层（方案文档 §4.2「慢网」）。
 *
 * 底层协议方法为 `Network.emulateNetworkConditions`——**该方法已被 CDP 标注 deprecated**
 * （方案 §4.2 明确要求封装适配层隔离此依赖：替代协议落地后只改本文件，spec 不动）。
 * 吞吐单位为 bytes/s，latency 为最小附加时延（ms）；`-1` 吞吐表示不限速。
 *
 * **仅 Chromium**：`browserContext.newCDPSession()` 是 Chromium 专属 API——
 * `playwright.live.config.ts` 唯一 project 即 Pixel 5（Chromium），非 Chromium
 * 运行时此处 newCDPSession 会直接抛错，如实失败（不静默跳过）。
 */
export interface SlowNetworkProfile {
  /** 最小附加时延（ms，请求与响应各计一次量级）。 */
  latencyMs: number
  /** 下行吞吐（bytes/s）。 */
  downloadBytesPerSecond: number
  /** 上行吞吐（bytes/s）。 */
  uploadBytesPerSecond: number
}

/**
 * 慢速蜂窝网近似（量级参考 DevTools "Slow 3G" 预设：~400ms 时延 + 几十 KB/s）。
 * 此处时延取 600ms：足以让 loading 态可被断言观测（≥1.2s 窗口），又保持整个请求
 * 往返明显低于 live 超时注入的推荐值 2000ms（见 network-resilience.spec.ts 场景 2），
 * 慢网场景不得撞上超时红线——它验证的是 loading 呈现，不是超时。
 */
export const SLOW_CELLULAR: SlowNetworkProfile = {
  latencyMs: 600,
  downloadBytesPerSecond: 50 * 1024,
  uploadBytesPerSecond: 32 * 1024,
}

/**
 * 对该 page 施加网络节流，返回恢复函数（吞吐 -1 = 解除限速，并 detach CDP session）。
 * 调用方须在 finally 中恢复，避免节流泄漏到后续断言。
 */
export async function applySlowNetwork(
  page: Page,
  profile: SlowNetworkProfile = SLOW_CELLULAR,
): Promise<() => Promise<void>> {
  const session = await page.context().newCDPSession(page)
  await session.send('Network.enable')
  await session.send('Network.emulateNetworkConditions', {
    offline: false,
    latency: profile.latencyMs,
    downloadThroughput: profile.downloadBytesPerSecond,
    uploadThroughput: profile.uploadBytesPerSecond,
  })
  return async () => {
    await session.send('Network.emulateNetworkConditions', {
      offline: false,
      latency: 0,
      downloadThroughput: -1,
      uploadThroughput: -1,
    })
    await session.detach()
  }
}
