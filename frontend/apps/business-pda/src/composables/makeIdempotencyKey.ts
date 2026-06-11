// 写操作（收货/复核/盘点 complete）需要幂等键以防重复提交。
// 优先用浏览器原生 crypto.randomUUID（jsdom/真机 WebView 均可用）；
// 不可用时回退到时间戳 + 高精度计时器；连 performance 都没有时退化到自增计数器，
// 仍保证同一会话内单调不重复。
let fallbackCounter = 0

export function makeIdempotencyKey(): string {
  const cryptoApi = globalThis.crypto
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') {
    return cryptoApi.randomUUID()
  }

  const perf = globalThis.performance
  const tick = perf && typeof perf.now === 'function' ? Math.trunc(perf.now()) : (fallbackCounter += 1)
  return `idem-${Date.now()}-${tick}`
}
