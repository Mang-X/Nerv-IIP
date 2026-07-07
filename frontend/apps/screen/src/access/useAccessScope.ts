import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  FACTORIES,
  type LineRef,
  LINES,
  type WorkshopRef,
  workshopsByFactory,
} from '@/data/mock/masterdata'
import { DEFAULT_PERSONA_ID, PERSONAS, type ScreenKey } from '@/data/mock/scope'

/**
 * 大屏访问上下文（mock persona，IAM-ready）。
 * persona → 可见工厂/车间/产线/大屏；launcher 与各屏都读它决定可见范围。
 * 接真 IAM 后改为从 claims 派生，消费方无需改动。见 spec §1.2。
 */
export const useAccessScope = defineStore('screen-access-scope', () => {
  const personaId = ref(DEFAULT_PERSONA_ID)
  const persona = computed(() => PERSONAS.find((p) => p.id === personaId.value) ?? PERSONAS[0])

  const factories = computed(() => FACTORIES.filter((f) => persona.value.factoryIds.includes(f.id)))
  const currentFactoryId = ref(factories.value[0]?.id ?? FACTORIES[0].id)

  const visibleWorkshops = computed<WorkshopRef[]>(() => {
    const all = workshopsByFactory(currentFactoryId.value)
    const ids = persona.value.workshopIds
    return ids === 'all' ? all : all.filter((w) => ids.includes(w.id))
  })

  const visibleLines = computed<LineRef[]>(() => {
    const wsIds = new Set(visibleWorkshops.value.map((w) => w.id))
    const all = LINES.filter((l) => wsIds.has(l.workshopId))
    const ids = persona.value.lineIds
    return ids === 'all' ? all : all.filter((l) => ids.includes(l.id))
  })

  const allowedScreens = computed<ScreenKey[]>(() => persona.value.allowedScreens)
  function canSeeScreen(k: ScreenKey): boolean {
    return allowedScreens.value.includes(k)
  }

  function switchFactory(id: string): void {
    if (factories.value.some((f) => f.id === id)) currentFactoryId.value = id
  }

  function setPersona(id: string): void {
    if (!PERSONAS.some((p) => p.id === id)) return
    personaId.value = id
    currentFactoryId.value = factories.value[0]?.id ?? currentFactoryId.value
  }

  return {
    persona,
    personaId,
    factories,
    currentFactoryId,
    visibleWorkshops,
    visibleLines,
    allowedScreens,
    canSeeScreen,
    switchFactory,
    setPersona,
  }
})
