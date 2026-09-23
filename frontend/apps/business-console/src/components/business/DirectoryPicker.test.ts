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
  props: { open: Boolean, context: { type: Object, default: undefined } },
  emits: ['update:open', 'created'],
  setup(props, { emit }) {
    // 真实弹窗在 setup 里按 context 预填表单：这里同样只在创建实例时读一次。
    const prefilled = JSON.stringify(props.context ?? null)
    const workCenters = useMasterDataResource<Record<string, unknown>>('work-center')
    async function save() {
      const response = await workCenters.create({ name: '总装二线工作中心' })
      emit('created', { code: response.data!.code, name: response.data!.displayName })
      emit('update:open', false)
    }
    return () =>
      props.open
        ? h('div', [
            h('button', {
              type: 'button',
              'data-testid': 'save',
              'data-context': prefilled,
              onClick: save,
            }),
            h('button', {
              type: 'button',
              'data-testid': 'cancel',
              onClick: () => emit('update:open', false),
            }),
          ])
        : null
  },
})

vi.mock('./directoryCreators', () => ({
  directoryCreatorFor: (type: string) =>
    type === 'work-center' || type === 'shift' || type === 'station'
      ? { permission: 'business.masterdata.resources.manage', dialog: StubCreateDialog }
      : undefined,
}))

interface Recorded {
  method: string
  url: URL
}

const mounted: Array<ReturnType<typeof mount>> = []

function harness(props: {
  creatable?: boolean
  directoryType?: 'work-center' | 'shift' | 'workshop' | 'station'
  placeholder?: string
  createContext?: Record<string, string>
  parent?: Record<string, string>
  /** 目录 / 列表的行（默认一条工作中心）；新建项刷新回来时挂在 LINE-A 下。 */
  rows?: Array<{ code: string; displayName: string; lineCode?: string }>
  /** 新建之后的刷新结果里带上新建项（现实里的列表 / 目录会把它刷回来）。 */
  refreshIncludesCreated?: boolean
}) {
  const requests: Recorded[] = []
  let created = false
  configureApiClient({
    baseUrl: 'http://gateway.local',
    fetch: (async (request: Request) => {
      const url = new URL(request.url)
      requests.push({ method: request.method, url })
      if (request.method === 'POST') {
        created = true
        return Response.json({
          success: true,
          data: { resourceType: 'work-center', code: 'WC-0042', displayName: '总装二线工作中心' },
        })
      }
      // 目录第一页里没有新建项（目录比一页多、或还没刷新到），选中后仍须显示名称。
      const items: Array<{ code: string; displayName: string; lineCode?: string }> = [
        ...(props.rows ?? [{ code: 'WC-0001', displayName: '冲压一线工作中心' }]),
      ]
      if (created && props.refreshIncludesCreated) {
        items.unshift({ code: 'WC-0042', displayName: '总装二线工作中心', lineCode: 'LINE-A' })
      }
      // 可搜目录回 `items`，基础数据资源列表回 `resources`。
      const rows = url.pathname.includes('/directories/') ? { items } : { resources: items }
      return Response.json({ success: true, data: { ...rows, total: 120 } })
    }) as typeof fetch,
  })
  const pinia = createPinia()
  const model = ref('')
  const createContext = ref(props.createContext)
  const parent = ref(props.parent)
  const wrapper = mount(
    defineComponent({
      setup() {
        useBusinessContextStore().patchContext({ organizationId: 'org-a', environmentId: 'env-a' })
        return () =>
          h(DirectoryPicker, {
            directoryType: props.directoryType ?? 'work-center',
            creatable: props.creatable,
            placeholder: props.placeholder,
            createContext: createContext.value,
            parent: parent.value,
            modelValue: model.value,
            'onUpdate:modelValue': (value: string) => (model.value = value),
          })
      },
    }),
    { global: { plugins: [pinia, PiniaColada] }, attachTo: document.body },
  )
  mounted.push(wrapper)
  return { createContext, model, parent, requests, wrapper }
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
    // 用例中途失败也要卸载，免得残留节点把后面的用例连带弄崩。
    for (const wrapper of mounted.splice(0)) wrapper.unmount()
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
      for (const wrapper of mounted.splice(0)) wrapper.unmount()
    }
  })

  // 新增弹窗和选择器并列成两个根节点后，调用方给的属性仍要落到选择器上、并覆盖默认文案。
  it('调用方的占位文案仍覆盖默认文案', async () => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { wrapper } = harness({ creatable: true, placeholder: '全部工作中心' })
    await flushPromises()
    expect(wrapper.get('button[aria-haspopup]').text()).toBe('全部工作中心')
  })

  it('没注册新增弹窗的类型即使 creatable 也没有入口', async () => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { wrapper } = harness({ creatable: true, directoryType: 'workshop' })
    await flushPromises()
    await openPicker(wrapper)
    expect(
      [...document.body.querySelectorAll('button')].some((b) => b.textContent?.includes('新增')),
    ).toBe(false)
  })

  // 工作中心走网关可搜目录；班次走基础数据资源列表。两条取数路径都要在新建后刷新、并显示名称。
  it.each([
    {
      directoryType: 'work-center' as const,
      noun: '工作中心',
      read: { path: '/directories/work-center' },
    },
    {
      directoryType: 'shift' as const,
      noun: '班次',
      read: { path: '/master-data/resources', resourceType: 'shift' },
    },
  ])('$noun：新建后自动选中新建项、显示名称，并刷新候选', async ({ directoryType, noun, read }) => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { model, requests, wrapper } = harness({ creatable: true, directoryType })
    await flushPromises()
    await openPicker(wrapper)

    createEntry(noun)!.click()
    await flushPromises()
    const readsBefore = requests.filter(
      (r) =>
        r.method === 'GET' &&
        r.url.pathname.endsWith(read.path) &&
        (!read.resourceType || r.url.searchParams.get('resourceType') === read.resourceType),
    )
    expect(readsBefore.length).toBeGreaterThan(0)
    document.body.querySelector<HTMLButtonElement>('[data-testid="save"]')!.click()
    await flushPromises()

    expect(model.value).toBe('WC-0042')
    const trigger = wrapper.get('button[aria-haspopup]')
    expect(trigger.text()).toContain('总装二线工作中心')
    expect(trigger.text()).toContain('WC-0042')
    const readsAfter = requests.filter(
      (r) =>
        r.method === 'GET' &&
        r.url.pathname.endsWith(read.path) &&
        (!read.resourceType || r.url.searchParams.get('resourceType') === read.resourceType),
    )
    expect(readsAfter.length).toBeGreaterThan(readsBefore.length)
  })

  // 刷新回来的列表里已经有新建项时，候选里只出现一次。
  it('班次：刷新结果带回新建项时候选里只有一条', async () => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { wrapper } = harness({
      creatable: true,
      directoryType: 'shift',
      refreshIncludesCreated: true,
    })
    await flushPromises()
    await openPicker(wrapper)
    createEntry('班次')!.click()
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="save"]')!.click()
    await flushPromises()

    await openPicker(wrapper)
    const rows = [...document.body.querySelectorAll('[role="option"]')].filter((row) =>
      row.textContent?.includes('WC-0042'),
    )
    expect(rows).toHaveLength(1)
  })

  // 父级预填：调用方给的上下文原样交给新增弹窗（如设备表单已选产线，新增工位时带上它）。
  it('create-context 原样传给新增弹窗', async () => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { wrapper } = harness({ creatable: true, createContext: { lineCode: 'LINE-01' } })
    await flushPromises()
    await openPicker(wrapper)
    createEntry()!.click()
    await flushPromises()

    const save = document.body.querySelector<HTMLButtonElement>('[data-testid="save"]')!
    expect(JSON.parse(save.dataset.context!)).toEqual({ lineCode: 'LINE-01' })
  })

  // 取消后上级改了再点新增，弹窗要按新的上级预填，而不是沿用上一次的实例。
  it('取消后换一个 context 再打开，弹窗拿到的是新的 context', async () => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { wrapper, createContext } = harness({
      creatable: true,
      createContext: { lineCode: 'LINE-01' },
    })
    await flushPromises()
    await openPicker(wrapper)
    createEntry()!.click()
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="cancel"]')!.click()
    await flushPromises()
    expect(document.body.querySelector('[data-testid="save"]')).toBeNull()

    createContext.value = { lineCode: 'LINE-02' }
    await flushPromises()
    await openPicker(wrapper)
    createEntry()!.click()
    await flushPromises()

    const save = document.body.querySelector<HTMLButtonElement>('[data-testid="save"]')!
    expect(JSON.parse(save.dataset.context!)).toEqual({ lineCode: 'LINE-02' })
  })

  // 设备表单里选了产线，工位只列这条产线下的；目录没有按产线收窄的参数，改取整表在本地收窄。
  it('按上级收窄：只列所选产线下的工位，换产线候选跟着换', async () => {
    state.permissionCodes = []
    const { parent, requests, wrapper } = harness({
      directoryType: 'station',
      parent: { lineCode: 'LINE-A' },
      rows: [
        { code: 'ST-A1', displayName: '前桥线一号工位', lineCode: 'LINE-A' },
        { code: 'ST-B1', displayName: '后桥线一号工位', lineCode: 'LINE-B' },
      ],
    })
    await flushPromises()
    expect(
      requests.some(
        (r) =>
          r.url.pathname.endsWith('/master-data/resources') &&
          r.url.searchParams.get('resourceType') === 'station',
      ),
    ).toBe(true)
    await openPicker(wrapper)
    const optionText = () =>
      [...document.body.querySelectorAll('[role="option"]')].map((row) => row.textContent)
    expect(optionText().join()).toContain('ST-A1')
    expect(optionText().join()).not.toContain('ST-B1')

    parent.value = { lineCode: 'LINE-B' }
    await flushPromises()
    expect(optionText().join()).toContain('ST-B1')
    expect(optionText().join()).not.toContain('ST-A1')
  })

  // 新建项在列表刷新回来之前先补进候选；刷新回来后同样按上级收窄，换了产线就不能再出现。
  it('新建项刷新回来后按上级收窄，换产线后不再出现', async () => {
    state.permissionCodes = ['business.masterdata.resources.manage']
    const { model, parent, wrapper } = harness({
      creatable: true,
      directoryType: 'station',
      parent: { lineCode: 'LINE-A' },
      rows: [{ code: 'ST-B1', displayName: '后桥线一号工位', lineCode: 'LINE-B' }],
      refreshIncludesCreated: true,
    })
    await flushPromises()
    await openPicker(wrapper)
    createEntry('工位')!.click()
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="save"]')!.click()
    await flushPromises()
    expect(model.value).toBe('WC-0042')

    parent.value = { lineCode: 'LINE-B' }
    await flushPromises()
    await openPicker(wrapper)
    const optionText = [...document.body.querySelectorAll('[role="option"]')]
      .map((row) => row.textContent)
      .join()
    expect(optionText).toContain('ST-B1')
    expect(optionText).not.toContain('WC-0042')
  })
})
