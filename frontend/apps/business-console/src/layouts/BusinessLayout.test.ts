import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { createBusinessConsoleI18n } from '@/i18n'
import { useAuthStore } from '@/stores/auth'
import IndexPage from '@/pages/index.vue'
import BusinessLayout from './BusinessLayout.vue'

const routeState = vi.hoisted(() => ({
  path: '/inventory/availability',
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ path: routeState.path, meta: { title: 'routes.availability' } }),
    useRouter: () => ({ push: vi.fn() }),
  }
})

interface Domain { id: string, title: string }
interface SideGroup { label?: string, items: { title: string }[] }

const AppShellTStub = defineComponent({
  name: 'AppShellT',
  props: {
    title: { type: String, required: true },
    topDomains: { type: Array, required: true },
    currentDomainId: { type: String, required: false },
    sideNav: { type: Array, required: false },
    user: { type: Object, required: false },
    signOutLabel: { type: String, required: false },
    maxVisibleDomains: { type: Number, required: false },
  },
  template: '<section><slot name="header-actions" /><slot /></section>',
})

function mountLayout(pinia = createPinia()) {
  return mount(BusinessLayout, {
    global: {
      plugins: [pinia, createBusinessConsoleI18n({ locale: 'en-US' })],
      stubs: { AppShellT: AppShellTStub, ThemePicker: true, ThemeToggle: true },
    },
    slots: { default: '<main>Business content</main>' },
  })
}

describe('BusinessLayout (T-shaped)', () => {
  beforeEach(() => {
    routeState.path = '/inventory/availability'
  })

  it('passes the T-shaped navigation model to AppShellT', () => {
    const wrapper = mountLayout()
    const shell = wrapper.getComponent(AppShellTStub)

    expect(shell.props('title')).toBe('Nerv-IIP 业务控制台')
    const domains = shell.props('topDomains') as Domain[]
    expect(domains.map((d) => d.id)).toEqual([
      'workbench', 'master-data', 'engineering', 'planning', 'mes', 'quality', 'inventory', 'wms', 'erp', 'equipment',
    ])
    // Current domain resolved from the route, with its domain-local side nav.
    expect(shell.props('currentDomainId')).toBe('inventory')
    const sideNav = shell.props('sideNav') as SideGroup[]
    const sideTitles = sideNav.flatMap((g) => g.items.map((i) => i.title))
    expect(sideTitles).toEqual(['库存可用量', '库存移动', '库存盘点'])
  })

  it('trims top domains and side navigation by principal permissions', () => {
    const pinia = createPinia()
    const auth = useAuthStore(pinia)
    auth.$patch({
      principal: {
        principalType: 'user',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionCodes: ['business.inventory.ledger.read'],
      },
    })

    const wrapper = mountLayout(pinia)
    const shell = wrapper.getComponent(AppShellTStub)

    const domains = shell.props('topDomains') as Domain[]
    expect(domains.map((d) => d.id)).toEqual(['workbench', 'inventory'])

    const sideNav = shell.props('sideNav') as SideGroup[]
    expect(sideNav.flatMap((g) => g.items.map((i) => i.title))).toEqual(['库存可用量'])
  })

  it('drops side navigation groups after permission trimming removes every item', () => {
    routeState.path = '/wms/wcs'
    const pinia = createPinia()
    const auth = useAuthStore(pinia)
    auth.$patch({
      principal: {
        principalType: 'user',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionCodes: ['business.wms.automation.manage'],
      },
    })

    const wrapper = mountLayout(pinia)
    const shell = wrapper.getComponent(AppShellTStub)

    const domains = shell.props('topDomains') as Domain[]
    expect(domains.map((d) => d.id)).toEqual(['workbench', 'wms'])

    const sideNav = shell.props('sideNav') as SideGroup[]
    expect(sideNav).toHaveLength(1)
    expect(sideNav[0]?.items.map((i) => i.title)).toEqual(['WCS 任务'])
  })

  it('resolves WMS routes to the 仓储作业 domain', () => {
    routeState.path = '/wms/inbound'
    const wrapper = mountLayout()
    const shell = wrapper.getComponent(AppShellTStub)

    expect(shell.props('currentDomainId')).toBe('wms')
    const sideNav = shell.props('sideNav') as SideGroup[]
    expect(sideNav.flatMap((g) => g.items.map((i) => i.title))).toEqual(['收货入库', '上架任务', '出库发货', '拣货任务', 'WCS 任务', '盘点执行'])
  })

  it('keeps MES foundation diagnostics in a separate side group under 制造执行', () => {
    routeState.path = '/mes/foundation'
    const wrapper = mountLayout()
    const shell = wrapper.getComponent(AppShellTStub)

    expect(shell.props('currentDomainId')).toBe('mes')
    const sideNav = shell.props('sideNav') as SideGroup[]
    const diagnostics = sideNav.find((g) => g.label === '追溯与诊断')
    expect(diagnostics?.items.map((i) => i.title)).toEqual(['追溯查询', '生产准备检查'])
    // Foundation must not sit in the primary plan/work-order group.
    const planning = sideNav.find((g) => g.label === '计划与工单')
    expect(planning?.items.some((i) => i.title === '生产准备检查')).toBe(false)
  })

  it('uses a fallback for the authenticated user display name', () => {
    const pinia = createPinia()
    const auth = useAuthStore(pinia)
    auth.$patch({
      principal: { principalType: 'user', organizationId: 'org-001', environmentId: 'env-dev' },
    })

    const wrapper = mountLayout(pinia)
    expect(wrapper.getComponent(AppShellTStub).props('user')).toMatchObject({ name: '已登录用户' })
  })

  it('renders the home page as a business workbench instead of a route directory', () => {
    const wrapper = mount(IndexPage, {
      global: {
        plugins: [createPinia(), createBusinessConsoleI18n({ locale: 'en-US' })],
        stubs: {
          BusinessLayout: { template: '<main><slot /></main>' },
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })

    const text = wrapper.text()
    expect(wrapper.findAll('section').length).toBeGreaterThanOrEqual(3)
    expect(wrapper.findAll('a').length).toBeGreaterThanOrEqual(8)

    const forbiddenTerms = ['demo', 'mock', 'seed', 'sourceSystem', 'operationId', '组织', '环境', '接口', '契约']
    for (const term of forbiddenTerms) {
      expect(text).not.toContain(term)
    }
  })
})
