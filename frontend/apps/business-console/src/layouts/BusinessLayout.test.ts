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
      { title: '主数据', items: [{ title: 'SKU 维护', to: { path: '/master-data/skus' } }] },
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
        title: 'MES',
        items: [
          { title: '生产驾驶舱', to: { path: '/mes' } },
          { title: '基础准备', to: { path: '/mes/foundation' } },
          { title: '生产计划', to: { path: '/mes/plans' } },
          { title: '计划与工单', to: { path: '/mes/work-orders' } },
          { title: '齐套与物料', to: { path: '/mes/materials' } },
          { title: '派工看板', to: { path: '/mes/dispatch' } },
          { title: '工序执行', to: { path: '/mes/operation-tasks' } },
          { title: '报工与完工', to: { path: '/mes/reports' } },
          { title: '质量与不良', to: { path: '/mes/quality' } },
          { title: '完工入库', to: { path: '/mes/receipts' } },
          { title: '规则排程', to: { path: '/mes/schedules' } },
          { title: '设备与停机', to: { path: '/mes/downtime' } },
          { title: '班次交接', to: { path: '/mes/handovers' } },
          { title: '追溯查询', to: { path: '/mes/traceability' } },
          { title: '产能影响', to: { path: '/mes/capacity' } },
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
