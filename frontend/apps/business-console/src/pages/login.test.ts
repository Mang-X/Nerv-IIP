import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createBusinessConsoleI18n } from '@/i18n'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { installAuthGuard } from '@/router/guards/auth'
import LoginPage from './login.vue'

const loginConsole = vi.hoisted(() => vi.fn())
vi.mock('@/api/auth', () => ({ consoleAuthApi: { loginConsole } }))

describe('business console login', () => {
  it.each([
    ['admin', 'role-platform-admin', P.notificationTasksRead, '/'],
    ['demo-buyer', 'role-erp-procurement', P.erpProcurementRead, '/erp'],
    ['demo-sales', 'role-erp-sales', P.erpSalesRead, '/erp/sales'],
    ['demo-finance', 'role-erp-finance', P.erpFinanceRead, '/erp/finance'],
    ['demo-planner', 'role-production-planner', P.planningDemandsRead, '/planning'],
    ['emp049', 'role-pda-warehouse', P.wmsReceiptsRead, '/wms/inbound'],
    ['emp010', 'role-pda-operator', P.mesOperationsRead, '/mes/operation-tasks'],
    ['emp012', 'role-pda-operator', P.mesOperationsRead, '/mes/operation-tasks'],
    ['demo-lead', 'role-mes-downtime-team-leader', P.mesDowntimeRead, '/mes/downtime'],
    ['emp034', 'role-pda-inspector', P.qualityInspectionRecordsRead, '/quality/inspection-tasks'],
    ['demo-qa-manager', 'role-quality-manager', P.qualityNcrRead, '/quality/ncrs'],
    [
      'demo-maintainer',
      'role-maintenance-technician',
      P.maintenanceWorkOrdersRead,
      '/maintenance/work-orders',
    ],
    ['demo-masterdata', 'role-masterdata-admin', P.masterDataProductsRead, '/master-data/skus'],
  ])(
    'lands %s on its job page after signing in from the root',
    async (loginName, roleId, read, path) => {
      localStorage.clear()
      const pinia = createPinia()
      setActivePinia(pinia)

      loginConsole.mockResolvedValue({
        accessToken: 'test-access-token',
        refreshToken: 'test-refresh-token',
        sessionId: 'test-session',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        principal: {
          loginName,
          roleIds: [roleId],
          permissionCodes: [read, P.masterDataResourcesRead],
        },
      })
      const router = createRouter({
        history: createMemoryHistory(),
        routes: [
          { path: '/login', name: '/login', component: LoginPage, meta: { guestOnly: true } },
          ...[...new Set(['/', path])].map((entry) => ({
            path: entry,
            component: { template: '<main />' },
            meta: { requiresAuth: true },
          })),
        ],
      })
      installAuthGuard(router)
      await router.push({ path: '/login', query: { redirect: '/' } })
      const wrapper = mount(LoginPage, {
        global: {
          plugins: [pinia, router, createBusinessConsoleI18n({ locale: 'zh-CN' })],
        },
      })

      await wrapper.get('input[name="loginName"]').setValue(loginName)
      await wrapper.get('input[name="password"]').setValue('fixture-password')
      await wrapper.get('form').trigger('submit')
      await flushPromises()

      expect(loginConsole).toHaveBeenLastCalledWith({ loginName, password: 'fixture-password' })
      expect(router.currentRoute.value.path).toBe(path)
      wrapper.unmount()
    },
  )
})
