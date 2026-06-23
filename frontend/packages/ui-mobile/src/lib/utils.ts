export { cn } from '@nerv-iip/ui'

/**
 * iOS-style rubber-band resistance. Maps a raw past-the-edge overscroll to a
 * damped distance that grows sub-linearly, so dragging beyond a boundary feels
 * elastic (stretchy, with mounting resistance) instead of dead-stopping. This is
 * the exact curve PullRefresh uses — shared so every draggable surface (bottom
 * sheet, swipe cell, pull-to-refresh) resists with an identical feel.
 */
export function rubberband(overscroll: number): number {
  return Math.sign(overscroll) * Math.abs(overscroll) ** 0.92 * 0.7
}

/**
 * Clamp `value` into [min, max], but let drags past either edge rubber-band:
 * in-range stays 1:1, beyond-range is damped via {@link rubberband}.
 */
export function clampRubber(value: number, min: number, max: number): number {
  if (value < min) return min - rubberband(min - value)
  if (value > max) return max + rubberband(value - max)
  return value
}
