import { onBeforeUnmount, ref, shallowRef } from 'vue'

/**
 * A cursor-following tooltip for the metric family's inline micro-vizzes
 * (bars / breakdown segments / target bar / ring). One instance per card; the
 * card renders a single teleported panel driven by `data` and `pos`, and calls
 * `move`/`hide` from the datapoints' pointer handlers. The unovis-based
 * sparkline uses its own native crosshair instead — this is only for the
 * hand-drawn vizzes where a full chart engine would be overkill.
 */

export interface MetricTooltipRow {
  label: string
  value: string
  /** Tailwind bg-* class for a leading colour swatch (breakdown legend parity). */
  swatchClass?: string
}

export interface MetricTooltipData {
  title?: string
  rows: MetricTooltipRow[]
}

export function useMetricTooltip() {
  const el = ref<HTMLElement | null>(null)
  const data = shallowRef<MetricTooltipData | null>(null)
  const pos = ref({ left: 0, top: 0 })

  function place(clientX: number, clientY: number) {
    const box = el.value?.getBoundingClientRect()
    const w = box?.width ?? 160
    const h = box?.height ?? 64
    let left = clientX + 14
    if (left + w > window.innerWidth - 8) left = clientX - w - 14
    let top = clientY - h - 12
    if (top < 8) top = clientY + 18
    pos.value = { left, top }
  }

  function move(event: MouseEvent, next: MetricTooltipData) {
    data.value = next
    place(event.clientX, event.clientY)
  }

  function hide() {
    data.value = null
  }

  /** Vue template-ref setter — captures the teleported panel for edge-flip measuring. */
  function setEl(node: unknown) {
    el.value = (node as HTMLElement | null) ?? null
  }

  onBeforeUnmount(hide)

  return { el, data, pos, move, hide, setEl }
}

/** The shape returned by {@link useMetricTooltip} — for the shared tip panel prop. */
export type UseMetricTooltip = ReturnType<typeof useMetricTooltip>
