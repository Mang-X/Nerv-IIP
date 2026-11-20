import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { IS_REAL_DATA } from '@/data/config'
import { SCREENS } from '@/data/screens'
import { useRealAuthStore } from '@/stores/realAuth'
import type { Persona } from '@/data/contracts/scope'
import type { LineRef, WorkshopRef } from '@/data/contracts/masterdata'
import type { ScreenKey } from '@/data/contracts/scope'
import { FACTORIES, LINES, workshopsByFactory } from '@/data/mock/masterdata'
import { DEFAULT_PERSONA_ID, PERSONAS } from '@/data/mock/scope'

/**
 * 大屏访问上下文（mock persona，IAM-ready）。
 * persona → 可见工厂/车间/产线/大屏；launcher 与各屏都读它决定可见范围。
 * 接真 IAM 后改为从 claims 派生，消费方无需改动。见 spec §1.2。
 */
export const useAccessScope = defineStore('screen-access-scope', () => {
  const auth = IS_REAL_DATA ? useRealAuthStore() : undefined
  const org = computed(() => auth?.principal?.organizationId ?? '')
  const personaId = ref(IS_REAL_DATA ? '' : DEFAULT_PERSONA_ID)
  const persona = computed<Persona>(() => IS_REAL_DATA ? { id: org.value, label: org.value, factoryIds: [], workshopIds: 'all', lineIds: 'all', allowedScreens: SCREENS.map((s) => s.key) } : PERSONAS.find((p) => p.id === personaId.value) ?? PERSONAS[0])

  const factories = computed(() => IS_REAL_DATA ? (org.value ? [{ id: org.value, name: org.value }] : []) : FACTORIES.filter((f) => persona.value.factoryIds.includes(f.id)))
  const selectedFactory = ref(factories.value[0]?.id ?? '')
  const currentFactoryId = computed({
    get: () => IS_REAL_DATA ? org.value : selectedFactory.value,
    set: (id: string) => { selectedFactory.value = id },
  })

  const visibleWorkshops = computed<WorkshopRef[]>(() => {
    if (IS_REAL_DATA) return []
    const all = workshopsByFactory(currentFactoryId.value)
    const ids = persona.value.workshopIds
    return ids === 'all' ? all : all.filter((w) => ids.includes(w.id))
  })

  const visibleLines = computed<LineRef[]>(() => {
    if (IS_REAL_DATA) return []
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
    if (IS_REAL_DATA || !PERSONAS.some((p) => p.id === id)) return
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
