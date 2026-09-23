import { configureApiClient } from '@nerv-iip/api-client'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { useMasterDataResource } from '@/composables/useBusinessMasterData'
import { useBusinessContextStore } from '@/stores/businessContext'
import DirectoryPicker from './DirectoryPicker.vue'

const state = vi.hoisted(() => ({ permissionCodes: [] as string[] }))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: state.permissionCodes } }),
}))

// 新增弹窗的替身：走真实的 `useMasterDataResource.create`，按约定把新建项的编码和名称发回去。
const StubCreateDialog = defineComponent({
  props: { open: Boolean },
  emits: ['update:open', 'created'],
  setup(props, { emit }) {
    const workCenters = useMasterDataResource<Record<string, unknown>>('work-center')
    async function save() {
      const response = await workCenters.create({ name: '总装二线工作中心' })
      emit('created', { code: response.data!.code, name: response.data!.displayName })
      emit('update:open', false)
    }
    return () =>
      props.open ? h('button', { type: 'button', 'data-testid': 'save', onClick: save }) : null
  },
})

vi.mock('./directoryCreators', () => ({
  directoryCreatorFor: (type: string) =>
    type === 'work-center' || type === 'shift'
      ? { permission: 'business.masterdata.resources.manage', dialog: StubCreateDialog }
      : undefined,
}))

interface Recorded {
  method: string
  url: URL
}

function harness(props: {
  creatable?: boolean
  directoryType?: 'work-center' | 'shift' | 'workshop'
  placeholder?: string
}) {
  const requests: Recorded[] = []
  configureApiClient({
    baseUrl: 'http://gateway.local',
    fetch: (async (request: Request) => {
      const url = new URL(request.url)
      requests.push({ method: request.method, url })
      if (request.method === 'POST') {
        return Response.json({
          success: true,
          data: { resourceType: 'work-center', code: 'WC-0042', displayName: '总装二线工作中心' },
        })
      }
      // 目录第一页里没有新建项（目录比一页多、或还没刷新到），选中后仍须显示名称。
      return Response.json({
        success: true,
        data: { items: [{ code: 'WC-0001', displayName: '冲压一线工作中心' }], total: 120 },
      })
    }) as typeof fetch,
  })
  const pinia = createPinia()
  const model = ref('')
  const wrapper = mount(
    defineComponent({
      setup() {
        useBusinessContextStore().patchContext({ organizationId: 'org-a', environmentId: 'env-a' })
        return () =>
          h(DirectoryPicker, {
            directoryType: props.directoryType ?? 'work-center',
            creatable: props.creatable,
            placeholder: props.placeholder,
            modelValue: model.value,
            'onUpdate:modelValue': (value: string) => (model.value = value),
          })
      },
    }),
    { global: { plugins: [pinia, PiniaColada] }, attachTo: document.body },
  )
  return { model, requests, wrapper }
}

async function openPicker(wrapper: ReturnType<typeof mount>) {
  await wrapper.get('button[aria-haspopup]').trigger('click')
  await flushPromises()
}

function createEntry(noun = '工作中心') {
  return [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => button.textContent?.trim() === `新增${noun}`,
  )
}

describe('DirectoryPicker 就地新增（#3796）', () => {
  afterEach(() => {
    configureApiClient()
    document.body.innerHTML = ''
  })

  it('有新增权限、类型注册了新增弹窗、调用方打开 creatable 时才出现入口', async () => {
    const cases = [
      { creatable: true, permissionCodes: ['business.masterdata.resources.manage'], shown: true },
      // 筛选区不开 creatable（owner 裁定）。
      { creatable: false, permissionCodes: ['business.masterdata.resources.manage'], shown: false },
      { creatable: true, permissionCodes: ['business.masterdata.resources.read'], shown: false },
    ]
    for (const { creatable, permissionCodes, shown } of cases) {
      state.permissionCodes = permissionCodes
      const { wrapper } = harness({ creatable })
      await flushPromises()
      await openPicker(wrapper)
      expect(createEntry() !== undefined).toBe(shown)
      wrapper.unmount()
    }
  })

  // 新增弹窗和选择器并列成两个根节点后，调用方给的属性仍要落到选择器上、并覆盖默认文案。
  it('调用方的占位文案仍覆盖默认文案', async () => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { wrapper } = harness({ creatable: true, placeholder: '全部工作中心' })
    await flushPromises()
    expect(wrapper.get('button[aria-haspopup]').text()).toBe('全部工作中心')
    wrapper.unmount()
  })

  it('没注册新增弹窗的类型即使 creatable 也没有入口', async () => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { wrapper } = harness({ creatable: true, directoryType: 'workshop' })
    await flushPromises()
    await openPicker(wrapper)
    expect(
      [...document.body.querySelectorAll('button')].some((b) => b.textContent?.includes('新增')),
    ).toBe(false)
    wrapper.unmount()
  })

  // 工作中心走网关可搜目录；班次走基础数据资源列表。两条取数路径都要在新建后刷新、并显示名称。
  it.each([
    { directoryType: 'work-center' as const, noun: '工作中心', read: '/directories/work-center' },
    { directoryType: 'shift' as const, noun: '班次', read: '/master-data/resources' },
  ])('$noun：新建后自动选中新建项、显示名称，并刷新候选', async ({ directoryType, noun, read }) => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { model, requests, wrapper } = harness({ creatable: true, directoryType })
    await flushPromises()
    await openPicker(wrapper)

    createEntry(noun)!.click()
    await flushPromises()
    const readsBefore = requests.filter((r) => r.method === 'GET' && r.url.pathname.endsWith(read))
    expect(readsBefore.length).toBeGreaterThan(0)
    document.body.querySelector<HTMLButtonElement>('[data-testid="save"]')!.click()
    await flushPromises()

    expect(model.value).toBe('WC-0042')
    const trigger = wrapper.get('button[aria-haspopup]')
    expect(trigger.text()).toContain('总装二线工作中心')
    expect(trigger.text()).toContain('WC-0042')
    const readsAfter = requests.filter((r) => r.method === 'GET' && r.url.pathname.endsWith(read))
    expect(readsAfter.length).toBeGreaterThan(readsBefore.length)
    wrapper.unmount()
  })
})
