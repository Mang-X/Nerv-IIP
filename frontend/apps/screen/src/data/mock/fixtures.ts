// 大屏 mock 的 seed 工具：受控随机抖动 + 真实感编号/时钟。

export function jitter(base: number, amp: number): number {
  return Math.round(base + (Math.random() - 0.5) * amp)
}

export function spark(n = 11): number[] {
  return Array.from({ length: n }, (_, i) => jitter(58 + i * 2.5, 14))
}

/** 距现在 minsAgo 分钟的 HH:mm（默认当前）。大屏展示用，非持久时间。 */
export function clock(minsAgo = 0): string {
  const d = new Date(Date.now() - minsAgo * 60_000)
  const p = (x: number) => String(x).padStart(2, '0')
  return `${p(d.getHours())}:${p(d.getMinutes())}`
}

/** 生成如 WO-000123 的真实感编号。 */
export function seq(prefix: string, n: number, pad = 4): string {
  return `${prefix}-${String(n).padStart(pad, '0')}`
}
