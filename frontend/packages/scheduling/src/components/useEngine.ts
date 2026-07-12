import { useColorMode } from '@nerv-iip/ui'
import { onBeforeUnmount, ref, watch, type Ref } from 'vue'
import type {
  EngineEvents,
  SchedulingEngine,
  SchedulingEngineOptions,
  TimeScale,
} from '../engine/engine'
import { DhtmlxEngine } from '../engine/dhtmlx/DhtmlxEngine'
import { isDhtmlxAvailable, preloadGantt } from '../engine/dhtmlx/loader'
import type { ScheduleModel } from '../model/types'

export type EngineKind = 'auto' | 'dhtmlx'

const TOKEN_NAMES = [
  '--nv-brand',
  '--destructive',
  '--nv-warning',
  '--nv-success',
  '--muted',
  '--muted-foreground',
  '--border',
  '--foreground',
  '--card',
]

function readTokens(): Record<string, string> {
  if (typeof document === 'undefined') return {}
  const s = getComputedStyle(document.documentElement)
  const out: Record<string, string> = {}
  for (const n of TOKEN_NAMES) {
    const v = s.getPropertyValue(n).trim()
    if (v) out[n] = v
  }
  return out
}

export interface UseEngineOptions {
  container: Ref<HTMLElement | undefined>
  model: Ref<ScheduleModel | undefined>
  view: 'order' | 'resource'
  scale: Ref<TimeScale>
  readOnly: Ref<boolean>
  groupBy?: Ref<string | undefined>
  engineKind?: EngineKind
  on?: Partial<{ [E in keyof EngineEvents]: (p: EngineEvents[E]) => void }>
}

export function useEngine(opts: UseEngineOptions) {
  const engine = ref<SchedulingEngine>()
  const engineName = ref<'dhtmlx' | 'unavailable'>('unavailable')
  const { isDark } = useColorMode()

  async function build(): Promise<SchedulingEngine | undefined> {
    await preloadGantt()
    const ok = await isDhtmlxAvailable()
    if (ok) {
      engineName.value = 'dhtmlx'
      return new DhtmlxEngine()
    }
    // 无 DHTMLX vendor(CI/文档构建/未配置本地试用包):不挂载任何引擎,组件显示占位。
    engineName.value = 'unavailable'
    return undefined
  }

  // 拖拽引起的 model 变更:引擎(DHTMLX)已就地移好条,不应再 setData 重建(否则条被收成线)。
  let suppressSetData = false

  async function init() {
    if (!opts.container.value || engine.value) return
    const e = await build()
    // 无可用引擎(DHTMLX vendor 缺失):不挂载,保持 engineName='unavailable',组件显示占位。
    if (!e) return
    // 容器可能在 await 期间被卸载。
    if (!opts.container.value) return
    const options: SchedulingEngineOptions = {
      view: opts.view,
      readOnly: opts.readOnly.value,
      scale: opts.scale.value,
      groupBy: opts.groupBy?.value,
      locale: 'zh',
      theme: { isDark: isDark.value, tokens: readTokens() },
    }
    e.mount(opts.container.value, options)
    for (const [name, cb] of Object.entries(opts.on ?? {})) {
      if (name === 'taskDragEnd') {
        e.on('taskDragEnd', ((p: { kind?: string }) => {
          // 工单甘特用 DHTMLX 原生拖拽:move/resize 已就地移好条,跳过 setData 避免重建破坏。
          // 资源排产板用自定义拖拽:原块静止,必须 setData 重新 parse 才能把卡片渲染到新时间/新泳道。
          suppressSetData = opts.view === 'order' && p?.kind !== 'reassign'
          ;(cb as (payload: unknown) => void)(p)
          setTimeout(() => {
            suppressSetData = false
          }, 80)
        }) as never)
      } else {
        e.on(name as keyof EngineEvents, cb as never)
      }
    }
    if (opts.model.value) e.setData(opts.model.value)
    engine.value = e
  }

  watch(opts.container, (el) => { if (el) void init() }, { immediate: true })
  watch(opts.model, (m) => {
    if (m && !suppressSetData) engine.value?.setData(m)
  })
  watch(opts.scale, (s) => engine.value?.applyCommand({ kind: 'scaleTo', scale: s }))
  watch(opts.readOnly, (r) => engine.value?.applyCommand({ kind: 'setReadOnly', readOnly: r }))
  if (opts.groupBy) {
    watch(opts.groupBy, (g) =>
      engine.value?.applyCommand({ kind: 'setGroupBy', groupBy: g ?? 'workCenter' }),
    )
  }
  watch(isDark, (d) =>
    engine.value?.applyCommand({ kind: 'setTheme', theme: { isDark: d, tokens: readTokens() } }),
  )

  onBeforeUnmount(() => {
    engine.value?.destroy()
    engine.value = undefined
  })

  return { engine, engineName }
}
