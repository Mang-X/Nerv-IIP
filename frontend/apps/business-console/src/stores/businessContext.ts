import { defineStore } from 'pinia'
import { computed, reactive } from 'vue'

export interface BusinessContextState {
  environmentId: string
  lineCode: string
  organizationId: string
  shiftCode: string
  siteCode: string
  workCenterCode: string
}

const DEFAULT_CONTEXT: BusinessContextState = {
  organizationId: 'org-001',
  environmentId: 'env-dev',
  siteCode: '',
  lineCode: '',
  workCenterCode: '',
  shiftCode: '',
}

export const useBusinessContextStore = defineStore('business-context', () => {
  const context = reactive<BusinessContextState>({ ...DEFAULT_CONTEXT })

  const organizationId = computed(() => context.organizationId)
  const environmentId = computed(() => context.environmentId)
  const siteCode = computed(() => context.siteCode)
  const lineCode = computed(() => context.lineCode)
  const workCenterCode = computed(() => context.workCenterCode)
  const shiftCode = computed(() => context.shiftCode)

  function patchContext(input: Partial<BusinessContextState>) {
    for (const [key, value] of Object.entries(input) as Array<
      [keyof BusinessContextState, string | undefined]
    >) {
      if (value === undefined) continue
      context[key] = value.trim()
    }
  }

  function clearExecutionScope() {
    context.siteCode = ''
    context.lineCode = ''
    context.workCenterCode = ''
    context.shiftCode = ''
  }

  function resetContext() {
    patchContext(DEFAULT_CONTEXT)
  }

  return {
    clearExecutionScope,
    context,
    environmentId,
    lineCode,
    organizationId,
    patchContext,
    resetContext,
    shiftCode,
    siteCode,
    workCenterCode,
  }
})
