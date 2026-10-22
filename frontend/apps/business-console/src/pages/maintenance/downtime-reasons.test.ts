import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, shallowRef } from 'vue'

import { useAuthStore } from '@/stores/auth'
import DowntimeReasonsPage from './downtime-reasons.vue'

const state = vi.hoisted(() => ({
  createReason: vi.fn(async (_body: Record<string, unknown>) => ({})),
  updateReason: vi.fn(async (_code: string, _body: Record<string, unknown>) => ({})),
  deleteReason: vi.fn(async (_code: string, _scope: Record<string, unknown>) => ({})),
}))

vi.mock('@/composables/useMaintenanceDowntimeReasonDirectory', () => ({
  useMaintenanceDowntimeReasonDirectory: () => ({
    keyword: shallowRef(''),
    reasons: computed(() => [
      {
        reasonCode: 'DT-MECH',
        description: '机械故障',
        reasonCategory: 'breakdown',
        lossCategory: 'availability',
      },
      {
        reasonCode: 'DT-PM',
        description: '计划保养',
        reasonCategory: 'planned',
        lossCategory: 'planned',
      },
    ]),
    options: computed(() => []),
    state: computed(() => 'ok'),
    message: computed(() => ''),
    total: computed(() => 2),
    refresh: vi.fn(),
  }),
}))

vi.mock('@/composables/useMaintenanceDowntimeReasonMutations', () => ({
  useMaintenanceDowntimeReasonMutations: () => ({
    createReason: state.createReason,
    updateReason: state.updateReason,
    deleteReason: state.deleteReason,
    saving: shallowRef(false),
    deleting: shallowRef(false),
  }),
}))

const passthrough = { template: '<div><slot /></div>' }
const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvSheet: passthrough,
  NvSheetContent: passthrough,
  NvSheetHeader: passthrough,
  NvSheetFooter: passthrough,
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvAlertDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvAlertDialogContent: passthrough,
  NvAlertDialogHeader: passthrough,
  NvAlertDialogFooter: passthrough,
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogCancel: { template: '<button type="button"><slot /></button>' },
  NvSearchSelect: {
    props: ['modelValue', 'id'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
}

function signInWith(permissionCodes: string[]) {
  useAuthStore().$patch({
    principal: { principalType: 'user', principalId: 'user-1', loginName: 'user', permissionCodes },
  })
}

async function setValue(wrapper: ReturnType<typeof mount>, selector: string, value: string) {
  await wrapper.get(selector).setValue(value)
  await flushPromises()
}

function button(wrapper: ReturnType<typeof mount>, text: string) {
  const found = wrapper.findAll('button').find((b) => b.text().includes(text))
  if (!found) throw new Error(`button ${text} not found`)
  return found
}

beforeEach(() => {
  setActivePinia(createPinia())
  signInWith([
    'business.maintenance.downtime-reasons.read',
    'business.maintenance.work-orders.manage',
  ])
  state.createReason.mockClear()
  state.updateReason.mockClear()
  state.deleteReason.mockClear()
})

describe('停机原因维护页（#3855）', () => {
  it('分类与损失类别按中文显示，不回吐码值', async () => {
    const wrapper = mount(DowntimeReasonsPage, { global: { stubs } })
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('机械故障')
    expect(text).toContain('设备故障')
    expect(text).toContain('可用率损失')
    expect(text).toContain('计划停机（不计损失）')
    expect(text).not.toContain('breakdown')
    expect(text).not.toContain('availability')
  })

  it('只读角色看不到新建、编辑与删除', async () => {
    signInWith(['business.maintenance.downtime-reasons.read'])
    const wrapper = mount(DowntimeReasonsPage, { global: { stubs } })
    await flushPromises()

    const labels = wrapper.findAll('button').map((b) => b.text())
    expect(labels.some((t) => t.includes('新建停机原因'))).toBe(false)
    expect(labels.some((t) => t === '编辑' || t === '删除')).toBe(false)
  })

  it('在抽屉里新建原因：重复编码被拦下，合法原因按受控码提交', async () => {
    const wrapper = mount(DowntimeReasonsPage, { global: { stubs } })
    await flushPromises()
    await button(wrapper, '新建停机原因').trigger('click')
    await flushPromises()

    await setValue(wrapper, '#dtr-code', 'DT-MECH')
    await setValue(wrapper, '#dtr-description', '液压系统故障')
    await setValue(wrapper, '#dtr-category', 'breakdown')
    await setValue(wrapper, '#dtr-loss', 'availability')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.createReason).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('该原因编码已存在')

    await setValue(wrapper, '#dtr-code', 'DT-HYD')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.createReason).toHaveBeenCalledWith({
      organizationId: expect.any(String),
      environmentId: expect.any(String),
      reasonCode: 'DT-HYD',
      description: '液压系统故障',
      reasonCategory: 'breakdown',
      lossCategory: 'availability',
    })
  })

  it('当前页没列出但服务端判定编码已存在：留在抽屉里提示，不报新建成功（#3855）', async () => {
    state.createReason.mockRejectedValueOnce({
      success: false,
      message: 'downtime-reason-code-already-exists',
      code: 409,
    })
    const wrapper = mount(DowntimeReasonsPage, { global: { stubs } })
    await flushPromises()
    await button(wrapper, '新建停机原因').trigger('click')
    await flushPromises()

    await setValue(wrapper, '#dtr-code', 'DT-OTHER-PAGE')
    await setValue(wrapper, '#dtr-description', '液压系统故障')
    await setValue(wrapper, '#dtr-category', 'breakdown')
    await setValue(wrapper, '#dtr-loss', 'availability')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.createReason).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('该原因编码已存在')
    expect(wrapper.find('#dtr-code').exists()).toBe(true)
  })

  it('编辑时原因编码只读，按原编码提交修改', async () => {
    const wrapper = mount(DowntimeReasonsPage, { global: { stubs } })
    await flushPromises()
    await wrapper
      .findAll('button')
      .filter((b) => b.text() === '编辑')[0]!
      .trigger('click')
    await flushPromises()

    expect(wrapper.find('#dtr-code').exists()).toBe(false)
    await setValue(wrapper, '#dtr-description', '机械故障（传动）')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.updateReason).toHaveBeenCalledWith(
      'DT-MECH',
      expect.objectContaining({
        description: '机械故障（传动）',
        reasonCategory: 'breakdown',
        lossCategory: 'availability',
      }),
    )
  })

  it('删除要确认，确认后按原因编码删除', async () => {
    const wrapper = mount(DowntimeReasonsPage, { global: { stubs } })
    await flushPromises()
    await wrapper
      .findAll('button')
      .filter((b) => b.text() === '删除')[1]!
      .trigger('click')
    await flushPromises()
    expect(state.deleteReason).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('删除停机原因「计划保养」？')

    const confirm = wrapper
      .findAll('button')
      .filter((b) => b.text() === '删除')
      .at(-1)!
    await confirm.trigger('click')
    await flushPromises()
    expect(state.deleteReason).toHaveBeenCalledWith('DT-PM', {
      organizationId: expect.any(String),
      environmentId: expect.any(String),
    })
  })
})
