import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { createBusinessConsoleI18n } from '@/i18n'
import { useAuthStore } from '@/stores/auth'
import BusinessLayout from './BusinessLayout.vue'

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ path: '/inventory/availability', meta: { title: 'routes.availability' } }),
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
  it('passes business domain navigation to AppShell', () => {
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
        title: '主数据',
        items: [
          { title: '物料与产品', to: { path: '/master-data/skus' } },
          { title: '客户与供应商', to: { path: '/master-data/partners' } },
          { title: '工厂资源', to: { path: '/master-data/resources' } },
          { title: '工艺与版本', to: { path: '/master-data/process' } },
        ],
      },
      {
        title: '库存',
        isActive: true,
        items: [
          { title: '库存可用量', to: { path: '/inventory/availability' } },
          { title: '库存移动', to: { path: '/inventory/movements' } },
          { title: '库存盘点', to: { path: '/inventory/counts' } },
        ],
      },
      {
        title: '质量',
        items: [
          { title: '检验任务与记录', to: { path: '/quality/inspections' } },
          { title: '不合格品处理', to: { path: '/quality/ncrs' } },
        ],
      },
      {
        title: 'ERP',
        items: [
          { title: '业务协同', to: { path: '/erp' } },
        ],
      },
      {
        title: 'MES',
        items: [
          { title: '生产驾驶舱', to: { path: '/mes' } },
          { title: '生产计划', to: { path: '/mes/plans' } },
          { title: '工单与派工', to: { path: '/mes/work-orders' } },
          { title: '工序执行', to: { path: '/mes/operation-tasks' } },
          { title: '在制跟踪', to: { path: '/mes/wip' } },
          { title: '报工记录', to: { path: '/mes/production-reports' } },
          { title: '完工入库', to: { path: '/mes/receipts' } },
          { title: '异常与产能', to: { path: '/mes/capacity' } },
          { title: '规则排程', to: { path: '/mes/schedules' } },
          { title: '生产准备检查', to: { path: '/mes/foundation' } },
        ],
      },
    ])
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
})
