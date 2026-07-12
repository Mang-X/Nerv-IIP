import { mount } from '@vue/test-utils'
import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import SchedulesPage from './schedules.vue'

vi.mock('@/composables/useBusinessMes', () => ({
  useMesSchedules: () => ({
    lastSchedule: computed(() => ({
      scheduleVersion: 42,
      trigger: 'Manual',
      scheduledAtUtc: '2026-07-03T08:00:00Z',
      assignments: [
        {
          workOrderId: 'WO-001',
          operationTaskId: 'OP-10',
          workCenterId: 'WC-01',
          startUtc: '2026-07-03T09:00:00Z',
          endUtc: '2026-07-03T10:00:00Z',
          reason: '手动触发',
        },
      ],
      affectedWorkOrderIds: ['WO-001'],
    })),
    runSchedule: vi.fn(),
    runScheduleError: ref(undefined),
    runSchedulePending: ref(false),
  }),
}))

vi.mock('@/stores/businessContext', () => ({
  useBusinessContextStore: () => ({
    organizationId: 'org',
    environmentId: 'dev',
  }),
}))

const stubs = {
  BusinessLayout: {
    template: '<main><slot /></main>',
  },
  NvButton: {
    template: '<button v-bind="$attrs"><slot /></button>',
  },
  NvDataTable: {
    props: ['rows', 'columns', 'emptyMessage'],
    template: '<section>{{ emptyMessage }}<div v-for="row in rows" :key="row.operationTaskId">{{ row.workOrderId }} {{ row.workCenterId }}</div></section>',
  },
  NvDialog: {
    props: ['open'],
    template: '<div><slot /></div>',
  },
  NvDialogContent: {
    template: '<div><slot /></div>',
  },
  NvDialogDescription: {
    template: '<p><slot /></p>',
  },
  NvDialogFooter: {
    template: '<div><slot /></div>',
  },
  NvDialogHeader: {
    template: '<div><slot /></div>',
  },
  NvDialogTitle: {
    template: '<h2><slot /></h2>',
  },
  NvField: {
    template: '<div><slot /></div>',
  },
  NvFieldGroup: {
    template: '<div><slot /></div>',
  },
  NvFieldLabel: {
    template: '<label><slot /></label>',
  },
  PageHeader: {
    props: ['title', 'breadcrumbs', 'count'],
    template: '<header><h1>{{ title }}</h1><p>{{ count }}</p><slot name="actions" /></header>',
  },
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="typeof to === \'string\' ? to : to.path"><slot /></a>',
  },
  SectionCard: {
    props: ['description', 'value', 'hint'],
    template: '<div>{{ description }} {{ value }} {{ hint }}</div>',
  },
  SectionCards: {
    template: '<div><slot /></div>',
  },
  NvSelect: {
    template: '<div><slot /></div>',
  },
  NvSelectContent: {
    template: '<div><slot /></div>',
  },
  NvSelectItem: {
    props: ['value'],
    template: '<div><slot /></div>',
  },
  NvSelectTrigger: {
    template: '<button><slot /></button>',
  },
  // NvSelectValue resolves to the underlying reka SelectValue component name in VTU.
  SelectValue: {
    template: '<span />',
  },
  Spinner: true,
  NvStatusBadge: {
    props: ['label'],
    template: '<span>{{ label }}</span>',
  },
}

describe('MES rule scheduling page IA copy', () => {
  it('states the transition boundary and links formal APS/Gantt work to Scheduling', () => {
    const wrapper = mount(SchedulesPage, { global: { stubs } })
    const text = wrapper.text()

    expect(text).toContain('规则排程（过渡）')
    expect(text).toContain('正式 APS / 甘特')
    expect(text).toContain('排产工作台')
    expect(text).not.toContain('APS 权威')

    const schedulingLink = wrapper
      .findAll('[data-router-link]')
      .find((link) => link.attributes('data-to') === '/scheduling')

    expect(schedulingLink).toBeDefined()
    expect(schedulingLink!.text()).toContain('排产工作台')
  })
})
