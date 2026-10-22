import type { SchedulePlanFreezeContextContract } from '@nerv-iip/api-client'

/** 服务端已算好的工作中心稳定窗口；只做时间轴投影，不判断工序是否被冻结。 */
export function freezeWindow(
  context: SchedulePlanFreezeContextContract | null | undefined,
  workCenterId: string,
) {
  if (!context) return undefined
  const startUtc = context.asOfUtc ?? ''
  const endUtc =
    context.workCenterWindows?.find((window) => window.workCenterId === workCenterId)?.endUtc ??
    context.defaultWindowEndUtc ??
    ''
  const start = Date.parse(startUtc)
  const end = Date.parse(endUtc)
  // 公共读合同字段可缺省；旧/不完整窗口不能伪造一段可见时间。
  if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) return undefined
  return { startUtc, endUtc, start, end }
}

export function projectFreezeWindow(
  window: NonNullable<ReturnType<typeof freezeWindow>>,
  axisStart: number,
  axisEnd: number,
) {
  const start = Math.max(window.start, axisStart)
  const end = Math.min(window.end, axisEnd)
  if (end <= start) return undefined
  return {
    ...window,
    left: ((start - axisStart) / (axisEnd - axisStart)) * 100,
    width: ((end - start) / (axisEnd - axisStart)) * 100,
  }
}
