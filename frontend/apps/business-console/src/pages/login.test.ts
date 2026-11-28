import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createBusinessConsoleI18n } from '@/i18n'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import LoginPage from './login.vue'

const loginConsole = vi.hoisted(() => vi.fn())
vi.mock('@/api/auth', () => ({ consoleAuthApi: { loginConsole } }))

describe('business console login', () => {
  it.each([
    ['buyer', P.erpProcurementRead, P.erpProcurementManage, '/erp'],
    ['sales', P.erpSalesRead, P.erpSalesManage, '/erp/sales'],
    ['finance', P.erpFinanceRead, P.erpFinanceManage, '/erp/finance'],
    ['planner', P.planningDemandsRead, 'business.planning.demands.manage', '/planning'],
    ['lead', P.mesDowntimeRead, P.mesDowntimeManage, '/mes/downtime'],
    [
      'maintainer',
      P.maintenanceWorkOrdersRead,
      P.maintenanceWorkOrdersManage,
      '/maintenance/work-orders',
    ],
  ])(
    'lands demo-%s on its job page after signing in from the root',
    async (job, read, manage, path) => {
      const loginName = `demo-${job}`
      loginConsole.mockResolvedValue({
        accessToken: 'test-access-token',
        refreshToken: 'test-refresh-token',
        sessionId: 'test-session',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        principal: {
          loginName,
          permissionCodes: [read, manage, P.masterDataResourcesRead],
        },
      })
      const router = createRouter({
        history: createMemoryHistory(),
        routes: [
          { path: '/login', name: '/login', component: LoginPage },
          { path, component: { template: '<main />' } },
        ],
      })
      await router.push({ path: '/login', query: { redirect: '/' } })
      const wrapper = mount(LoginPage, {
        global: {
          plugins: [createPinia(), router, createBusinessConsoleI18n({ locale: 'zh-CN' })],
        },
      })

      await wrapper.get('input[name="loginName"]').setValue(loginName)
      await wrapper.get('input[name="password"]').setValue('fixture-password')
      await wrapper.get('form').trigger('submit')
      await flushPromises()

      expect(loginConsole).toHaveBeenLastCalledWith({ loginName, password: 'fixture-password' })
      expect(router.currentRoute.value.path).toBe(path)
    },
  )
})
