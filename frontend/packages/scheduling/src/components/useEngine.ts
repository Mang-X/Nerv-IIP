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
import { NativeEngine } from '../engine/native/NativeEngine'
import type { ScheduleModel } from '../model/types'

export type EngineKind = 'auto' | 'native' | 'dhtmlx'

const TOKEN_NAMES = [
  '--brand',
  '--destructive',
  '--warning',
  '--success',
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
  engineKind?: EngineKind
  on?: Partial<{ [E in keyof EngineEvents]: (p: EngineEvents[E]) => void }>
}

export function useEngine(opts: UseEngineOptions) {
  const engine = ref<SchedulingEngine>()
  const engineName = ref<'native' | 'dhtmlx'>('native')
  const { isDark } = useColorMode()

  async function build(): Promise<SchedulingEngine> {
    const kind = opts.engineKind ?? 'auto'
    if (kind === 'native') {
      engineName.value = 'native'
      return new NativeEngine()
    }
    await preloadGantt()
    const ok = await isDhtmlxAvailable()
    if (ok) {
      engineName.value = 'dhtmlx'
      return new DhtmlxEngine()
    }
    engineName.value = 'native'
    return new NativeEngine()
  }

  async function init() {
    if (!opts.container.value || engine.value) return
    const e = await build()
    // 容器可能在 await 期间被卸载。
    if (!opts.container.value) return
    const options: SchedulingEngineOptions = {
      view: opts.view,
      readOnly: opts.readOnly.value,
      scale: opts.scale.value,
      locale: 'zh',
      theme: { isDark: isDark.value, tokens: readTokens() },
    }
    e.mount(opts.container.value, options)
    for (const [name, cb] of Object.entries(opts.on ?? {})) {
      e.on(name as keyof EngineEvents, cb as never)
    }
    if (opts.model.value) e.setData(opts.model.value)
    engine.value = e
  }

  watch(opts.container, (el) => { if (el) void init() }, { immediate: true })
  watch(opts.model, (m) => { if (m) engine.value?.setData(m) })
  watch(opts.scale, (s) => engine.value?.applyCommand({ kind: 'scaleTo', scale: s }))
  watch(opts.readOnly, (r) => engine.value?.applyCommand({ kind: 'setReadOnly', readOnly: r }))
  watch(isDark, (d) =>
    engine.value?.applyCommand({ kind: 'setTheme', theme: { isDark: d, tokens: readTokens() } }),
  )

  onBeforeUnmount(() => {
    engine.value?.destroy()
    engine.value = undefined
  })

  return { engine, engineName }
}
