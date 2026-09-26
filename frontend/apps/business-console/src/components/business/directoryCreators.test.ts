import { configureApiClient } from '@nerv-iip/api-client'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import DirectoryPicker from './DirectoryPicker.vue'

// 真实注册表 + 真实新增弹窗（不替身），只替掉网络与权限。
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: {
      permissionCodes: [
        'business.masterdata.products.manage',
        'business.masterdata.resources.manage',
      ],
    },
  }),
}))

// 弹窗与下拉就地渲染（reka 的 portal 在 jsdom 卸载会崩）；下拉换成原生 <select> 以便填表。
const stubs = {
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

const CREATED = {
  material: { path: '/master-data/skus', code: 'SKU-0042', name: '不锈钢法兰盘' },
  shift: { path: '/master-data/shifts', code: 'SHIFT-0042', name: '中班' },
} as const

interface Recorded {
  method: string
  url: URL
  body?: Record<string, unknown>
}

const mounted: Array<ReturnType<typeof mount>> = []

function harness(directoryType: keyof typeof CREATED) {
  const requests: Recorded[] = []
  configureApiClient({
    baseUrl: 'http://gateway.local',
    fetch: (async (request: Request) => {
      const url = new URL(request.url)
      if (request.method === 'POST') {
        requests.push({ method: 'POST', url, body: await request.json() })
        const created = Object.values(CREATED).find((c) => url.pathname.endsWith(c.path))!
        return Response.json({
          success: true,
          data: { code: created.code, displayName: created.name },
        })
      }
      requests.push({ method: request.method, url })
      if (url.pathname.endsWith('/product-categories')) {
        return Response.json({
          success: true,
          data: { items: [{ categoryCode: 'PCAT-01', categoryName: '结构件', enabled: true }] },
        })
      }
      // 目录与资源列表都不含新建项：名称只能来自弹窗回传。
      return Response.json({ success: true, data: { items: [], resources: [], total: 0 } })
    }) as typeof fetch,
  })
  const model = ref('')
  const wrapper = mount(
    defineComponent({
      setup() {
        useBusinessContextStore().patchContext({ organizationId: 'org-a', environmentId: 'env-a' })
        return () =>
          h(DirectoryPicker, {
            directoryType,
            creatable: true,
            modelValue: model.value,
            'onUpdate:modelValue': (value: string) => (model.value = value),
          })
      },
    }),
    { global: { plugins: [createPinia(), PiniaColada], stubs }, attachTo: document.body },
  )
  mounted.push(wrapper)
  return { model, requests, wrapper }
}

async function openCreateDialog(wrapper: ReturnType<typeof mount>, noun: string) {
  await wrapper.get('button[aria-haspopup]').trigger('click')
  await flushPromises()
  const entry = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => button.textContent?.trim() === `新增${noun}`,
  )!
  entry.click()
  // 弹窗是异步组件：首次点入口才加载。
  await vi.dynamicImportSettled()
  await flushPromises()
}

function setInput(selector: string, value: string) {
  const input = document.body.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!
  input.value = value
  input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'))
}

function directoryReads(requests: Recorded[], directoryType: string) {
  return requests.filter(
    (r) => r.method === 'GET' && r.url.pathname.endsWith(`/directories/${directoryType}`),
  ).length
}

describe('已注册的新增弹窗（#3797）', () => {
  afterEach(() => {
    for (const wrapper of mounted.splice(0)) wrapper.unmount()
    configureApiClient()
    document.body.innerHTML = ''
  })

  it('物料：在选择器里新建后自动选中、显示名称，并刷新可搜目录', async () => {
    const { model, requests, wrapper } = harness('material')
    await flushPromises()
    await openCreateDialog(wrapper, '物料')

    setInput('#sku-name', '不锈钢法兰盘')
    const category = [...document.body.querySelectorAll('select')].find((s) =>
      [...s.options].some((o) => o.value === 'PCAT-01'),
    )!
    category.value = 'PCAT-01'
    category.dispatchEvent(new Event('change'))
    await flushPromises()

    const readsBefore = directoryReads(requests, 'material')
    expect(readsBefore).toBeGreaterThan(0)
    document.body.querySelector('form')!.requestSubmit()
    await flushPromises()

    const post = requests.find((r) => r.method === 'POST')!
    expect(post.body).toMatchObject({
      organizationId: 'org-a',
      environmentId: 'env-a',
      name: '不锈钢法兰盘',
      category: 'PCAT-01',
    })
    expect(model.value).toBe('SKU-0042')
    expect(wrapper.get('button[aria-haspopup]').text()).toContain('不锈钢法兰盘')
    // 选择器取的是可搜目录：新建之后必须重新拉，否则搜不到刚建的物料。
    expect(directoryReads(requests, 'material')).toBeGreaterThan(readsBefore)
  })

  it('班次：在选择器里新建后自动选中并显示名称', async () => {
    const { model, requests, wrapper } = harness('shift')
    await flushPromises()
    await openCreateDialog(wrapper, '班次')

    setInput('#shift-name', '中班')
    setInput('#shift-start', '16:00')
    setInput('#shift-end', '00:00')
    setInput('#shift-paid', '450')
    await flushPromises()
    document.body.querySelector('form')!.requestSubmit()
    await flushPromises()

    const post = requests.find((r) => r.method === 'POST')!
    expect(post.body).toMatchObject({
      organizationId: 'org-a',
      environmentId: 'env-a',
      name: '中班',
      startsAt: '16:00',
      endsAt: '00:00',
      paidMinutes: 450,
    })
    expect(model.value).toBe('SHIFT-0042')
    expect(wrapper.get('button[aria-haspopup]').text()).toContain('中班')
  })
})
