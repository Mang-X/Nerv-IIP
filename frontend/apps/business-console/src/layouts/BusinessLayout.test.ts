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

const AppShellStub = defineComponent({
  name: 'AppShell',
  props: {
    navItems: {
      required: true,
      type: Array,
    },
    navLabel: {
      required: false,
      type: String,
    },
    title: {
      required: true,
      type: String,
    },
    user: {
      required: false,
      type: Object,
    },
  },
  template: '<section><slot name="header" /><slot /></section>',
})

describe('BusinessLayout', () => {
  beforeEach(() => {
    routeState.path = '/inventory/availability'
  })

  it('passes workflow-oriented business navigation to AppShell', () => {
    const wrapper = mount(BusinessLayout, {
      global: {
        plugins: [createPinia(), createBusinessConsoleI18n({ locale: 'en-US' })],
        stubs: {
          AppShell: AppShellStub,
        },
      },
      slots: {
        default: '<main>Business content</main>',
      },
    })

    const navItems = wrapper.getComponent(AppShellStub).props('navItems') as Record<string, unknown>[]
    expect(wrapper.getComponent(AppShellStub).props('title')).toBe('Nerv-IIP 业务控制台')
    expect(wrapper.getComponent(AppShellStub).props('navLabel')).toBe('业务模块')
    expect(navItems).toMatchObject([
      {
        title: '基础数据',
        items: [
          { title: '物料与产品', to: { path: '/master-data/skus' } },
          { title: '客户与供应商', to: { path: '/master-data/partners' } },
          { title: '工厂资源', to: { path: '/master-data/resources' } },
        ],
      },
      {
        title: '工程资料',
        items: [
          { title: '工艺与版本', to: { path: '/master-data/process' } },
          { title: '发布工程版本', to: { path: '/engineering' } },
        ],
      },
      {
        title: '计划与采购',
        items: [
          { title: '需求与物料计划', to: { path: '/planning' } },
          { title: '采购与供应', to: { path: '/erp' } },
        ],
      },
      {
        title: '生产执行',
        items: [
          { title: '生产驾驶舱', to: { path: '/mes' } },
          { title: '生产计划', to: { path: '/mes/plans' } },
          { title: '工单与派工', to: { path: '/mes/work-orders' } },
          { title: '齐套与物料', to: { path: '/mes/materials' } },
          { title: '派工看板', to: { path: '/mes/dispatch' } },
          { title: '工序执行', to: { path: '/mes/operation-tasks' } },
          { title: '在制跟踪', to: { path: '/mes/wip' } },
          { title: '报工记录', to: { path: '/mes/production-reports' } },
          { title: '完工入库', to: { path: '/mes/receipts' } },
        ],
      },
      {
        title: '质量与库存',
        isActive: true,
        items: [
          { title: '检验任务与记录', to: { path: '/quality/inspections' } },
          { title: '不合格品处理', to: { path: '/quality/ncrs' } },
          { title: '质量与不良', to: { path: '/mes/quality' } },
          { title: '库存可用量', to: { path: '/inventory/availability' } },
          { title: '库存移动', to: { path: '/inventory/movements' } },
          { title: '库存盘点', to: { path: '/inventory/counts' } },
        ],
      },
      {
        title: '设备异常',
        items: [
          { title: '设备与停机', to: { path: '/mes/downtime' } },
          { title: '异常与产能', to: { path: '/mes/capacity' } },
          { title: '规则排程', to: { path: '/mes/schedules' } },
          { title: '班次交接', to: { path: '/mes/handovers' } },
        ],
      },
      {
        title: '追溯报表',
        items: [
          { title: '追溯查询', to: { path: '/mes/traceability' } },
          { title: '生产准备检查', to: { path: '/mes/foundation' } },
        ],
      },
    ])
    expect(navItems.find((item) => item.title === '追溯报表')?.icon).not.toBe(
      navItems.find((item) => item.title === '计划与采购')?.icon,
    )
  })

  it('keeps MES foundation diagnostics out of the primary production execution group', () => {
    routeState.path = '/mes/foundation'

    const wrapper = mount(BusinessLayout, {
      global: {
        plugins: [createPinia(), createBusinessConsoleI18n({ locale: 'en-US' })],
        stubs: {
          AppShell: AppShellStub,
        },
      },
    })

    const navItems = wrapper.getComponent(AppShellStub).props('navItems') as Record<string, unknown>[]
    expect(navItems.find((item) => item.title === '生产执行')).toMatchObject({ isActive: false })
    expect(navItems.find((item) => item.title === '追溯报表')).toMatchObject({
      isActive: true,
      items: [
        { title: '追溯查询', to: { path: '/mes/traceability' } },
        { title: '生产准备检查', to: { path: '/mes/foundation' } },
      ],
    })
  })

  it('uses a fallback for authenticated user display name', () => {
    const pinia = createPinia()
    const auth = useAuthStore(pinia)
    auth.$patch({
      principal: {
        principalType: 'user',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })

    const wrapper = mount(BusinessLayout, {
      global: {
        plugins: [pinia, createBusinessConsoleI18n({ locale: 'en-US' })],
        stubs: {
          AppShell: AppShellStub,
        },
      },
    })

    expect(wrapper.getComponent(AppShellStub).props('user')).toMatchObject({
      name: '已登录用户',
    })
  })

  it('uses a distinct breadcrumb label for procurement pages', () => {
    routeState.path = '/erp/purchase-orders'

    const wrapper = mount(BusinessLayout, {
      global: {
        plugins: [createPinia(), createBusinessConsoleI18n({ locale: 'en-US' })],
        stubs: {
          AppShell: AppShellStub,
        },
      },
    })

    expect(wrapper.text()).toContain('采购与供应')
    expect(wrapper.text()).not.toContain('计划与采购')
  })

  it('renders the home page as a business workbench instead of a route directory', () => {
    const wrapper = mount(IndexPage, {
      global: {
        plugins: [createPinia(), createBusinessConsoleI18n({ locale: 'en-US' })],
        stubs: {
          BusinessLayout: {
            template: '<main><slot /></main>',
          },
          RouterLink: {
            props: ['to'],
            template: '<a><slot /></a>',
          },
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
