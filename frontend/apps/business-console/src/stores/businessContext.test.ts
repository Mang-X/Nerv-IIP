import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { useBusinessContextStore } from './businessContext'

describe('business context store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('starts with the development organization and environment context', () => {
    const context = useBusinessContextStore()

    expect(context.organizationId).toBe('org-001')
    expect(context.environmentId).toBe('env-dev')
    expect(context.siteCode).toBe('')
    expect(context.lineCode).toBe('')
    expect(context.workCenterCode).toBe('')
    expect(context.shiftCode).toBe('')
  })

  it('trims and stores only the provided business context fields', () => {
    const context = useBusinessContextStore()

    context.patchContext({
      organizationId: ' org-002 ',
      environmentId: ' prod ',
      siteCode: ' S1 ',
      lineCode: ' L1 ',
      workCenterCode: ' WC-10 ',
      shiftCode: ' DAY ',
    })

    expect(context.context).toMatchObject({
      organizationId: 'org-002',
      environmentId: 'prod',
      siteCode: 'S1',
      lineCode: 'L1',
      workCenterCode: 'WC-10',
      shiftCode: 'DAY',
    })
  })

  it('resets optional execution scope without clearing organization or environment', () => {
    const context = useBusinessContextStore()
    context.patchContext({
      organizationId: 'org-002',
      environmentId: 'prod',
      siteCode: 'S1',
      lineCode: 'L1',
      workCenterCode: 'WC-10',
      shiftCode: 'DAY',
    })

    context.clearExecutionScope()

    expect(context.context).toMatchObject({
      organizationId: 'org-002',
      environmentId: 'prod',
      siteCode: '',
      lineCode: '',
      workCenterCode: '',
      shiftCode: '',
    })
  })
})
