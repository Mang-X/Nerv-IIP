import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, shallowRef } from 'vue'

import AlarmsPage from './alarms.vue'

const alarmState = vi.hoisted(() => ({
  alarms: [] as Array<Record<string, unknown>>,
  acknowledgeAlarm: vi.fn(() => Promise.resolve()),
  shelveAlarm: vi.fn(() => Promise.resolve()),
  unshelveAlarm: vi.fn(() => Promise.resolve()),
  refreshAlarms: vi.fn(() => Promise.resolve()),
}))

vi.mock('@/composables/useBusinessEquipment', () => ({
  useBusinessEquipmentAlarms: () => ({
    acknowledgeAlarm: alarmState.acknowledgeAlarm,
    alarms: computed(() => alarmState.alarms),
    alarmsError: shallowRef(),
    alarmsPending: shallowRef(false),
    refreshAlarms: alarmState.refreshAlarms,
    shelveAlarm: alarmState.shelveAlarm,
    unshelveAlarm: alarmState.unshelveAlarm,
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: {
      loginName: 'operator-a',
      permissionCodes: ['business.iiot.alarms.read', 'business.iiot.alarms.write'],
    },
  }),
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, useRouter: () => ({ push: vi.fn() }) }
})

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}

function seedAlarms() {
  alarmState.alarms = [
    {
      alarmEventId: 'ALM-1',
      deviceAssetId: 'DEV-OIL-01',
      alarmCode: 'TEMP-HIGH',
      severity: 'critical',
      status: 'raised',
      raisedAtUtc: '2026-07-12T01:00:00Z',
      escalatedAtUtc: '2026-07-12T02:00:00Z',
      escalationReason: '15 分钟未确认',
      escalationRecipientRefs: ['班组长', '设备主管'],
    },
    {
      alarmEventId: 'ALM-2',
      deviceAssetId: 'DEV-OIL-02',
      alarmCode: 'VIB-HIGH',
      severity: 'warning',
      status: 'raised',
      raisedAtUtc: '2026-07-12T01:10:00Z',
    },
    {
      alarmEventId: 'ALM-3',
      deviceAssetId: 'DEV-PACK-01',
      alarmCode: 'PRESSURE-LOW',
      severity: 'warning',
      status: 'raised',
      raisedAtUtc: '2026-07-12T01:20:00Z',
      acknowledgedAtUtc: '2026-07-12T01:30:00Z',
      acknowledgedBy: 'operator-b',
    },
    {
      alarmEventId: 'ALM-4',
      deviceAssetId: 'DEV-PACK-02',
      alarmCode: 'DOOR-OPEN',
      severity: 'info',
      status: 'shelved',
      raisedAtUtc: '2026-07-12T01:40:00Z',
      shelvedUntilUtc: '2026-07-12T03:40:00Z',
      shelvedBy: 'operator-c',
    },
  ]
}

describe('alarm ops depth (MAN-441 #795)', () => {
  beforeEach(() => {
    alarmState.acknowledgeAlarm.mockClear()
    alarmState.shelveAlarm.mockClear()
    alarmState.refreshAlarms.mockClear()
    seedAlarms()
  })

  it('flags escalated alarms with a single red escalation icon in the row', () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })

    const icons = wrapper.findAll('[aria-label="报警已升级"]')
    // Only the escalated alarm (ALM-1) carries the marker.
    expect(icons).toHaveLength(1)
    expect(icons[0].classes()).toContain('text-destructive')
  })

  it('de-emphasizes acknowledged / shelved rows but keeps active + escalated rows solid', () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    const rowClassOf = (id: string) => {
      const row = wrapper.findAll('tbody tr').find((r) => r.text().includes(id))
      return row?.classes() ?? []
    }

    expect(rowClassOf('ALM-1')).not.toContain('opacity-55') // escalated → stays solid
    expect(rowClassOf('ALM-2')).not.toContain('opacity-55') // raised → stays solid
    expect(rowClassOf('ALM-3')).toContain('opacity-55') // acknowledged → dimmed
    expect(rowClassOf('ALM-4')).toContain('opacity-55') // shelved → dimmed
  })

  it('surfaces the escalated quick-filter tab', () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    expect(wrapper.text()).toContain('已升级')
  })

  it('shows batch-action counts scoped to eligible rows when all rows are selected', async () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })

    await wrapper.find('[aria-label="全选"]').trigger('click')
    await nextTick()

    const text = wrapper.text()
    // ack targets = ALM-1, ALM-2, ALM-4 (ALM-3 already acknowledged) → 3
    expect(text).toContain('批量确认 (3)')
    // shelve targets = ALM-1, ALM-2, ALM-3 (ALM-4 already shelved) → 3
    expect(text).toContain('批量搁置 (3)')
  })
})
