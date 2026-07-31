import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, reactive } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'

import { bindWmsWorkScopeFilters } from './useWmsWorkScope'

/**
 * #1343 WMS 作业范围就绪链路（root cause 回归）。
 *
 * 仓储六页的读查询全部被 scope gate 拦在「有 scopeKind/scopeId 才发」。走查里
 * admin 整域 403 全空的真因是：WMS 服务侧要求作业池成员资格，种子只登记
 * user-emp-049，于是作业范围目录直接 403，前端拿不到任何可选范围——页面却把它
 * 说成「请先选择业务范围」/「暂无数据」。
 *
 * 服务端已改为：站点范围由 IAM 精确站点授权直接成立（见
 * WarehouseWorkScopeAuthorizer）。本文件守住前端这一侧的闭环：
 * 1. 拿到授权清单后自动选择（记住的选择优先，否则第一项），并把可信 kind/id 绑
 *    到列表筛选，列表查询才带范围参数发出；
 * 2. localStorage 记住的选择在仍被授权时优先生效，显式切换会被记住；
 * 3. 目录 403 / 零授权范围时绝不发查询，且给出的是**真实原因**，不含糊成
 *    「请稍后重试」，也不伪装成「暂无数据」。
 */

type CatalogQuery = { organizationId: string; environmentId: string }

const sdkState = vi.hoisted(() => ({
  catalogRequests: [] as Array<{
    query: CatalogQuery
    resolve: (value: unknown) => void
    reject: (reason: unknown) => void
  }>,
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  return {
    ...actual,
    getBusinessConsoleWmsReceiptWorkScopesQueryOptions: vi.fn(
      ({ query }: { query: CatalogQuery }) => ({
        key: ['wms-receipt-work-scopes', query.organizationId, query.environmentId],
        query: () =>
          new Promise((resolve, reject) => {
            sdkState.catalogRequests.push({ query, resolve, reject })
          }),
      }),
    ),
  }
})

const POOL_SCOPE = {
  scopeKind: 'work-pool',
  scopeId: 'WMS-SITE-001-RECEIVING',
  displayName: '一号仓收货作业池',
}
const SITE_SCOPE = { scopeKind: 'site', scopeId: 'SITE-001', displayName: '一号仓库' }

function resolveCatalog(index: number, items: Array<Record<string, string>>) {
  const request = sdkState.catalogRequests[index]
  expect(request).toBeDefined()
  request!.resolve({
    success: true,
    data: { actorPrincipalId: 'user-admin', items },
  })
}

function createHarness() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore(pinia)
  auth.principal = {
    principalId: 'user-admin',
    organizationId: 'org-001',
    environmentId: 'env-dev',
  } as never
  useBusinessContextStore(pinia).patchContext({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })

  const filters = reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skip: 40,
    scopeKind: undefined as string | undefined,
    scopeId: undefined as string | undefined,
  })
  let scope: ReturnType<typeof bindWmsWorkScopeFilters> | undefined
  const Harness = defineComponent({
    setup() {
      scope = bindWmsWorkScopeFilters(filters, 'receipts')
      return () => h('div', scope!.unreadyMessage.value || 'ready')
    },
  })
  const wrapper = mount(Harness, {
    global: { plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }]] },
  })
  if (!scope) throw new Error('WMS 作业范围 composable 未初始化')
  return { wrapper, filters, scope, auth }
}

describe('PC WMS 作业范围选择闭环 (#1343)', () => {
  beforeEach(() => {
    sdkState.catalogRequests.length = 0
    localStorage.clear()
  })

  it('目录返回后自动选择首项、绑定可信范围并重置分页', async () => {
    const { wrapper, filters, scope } = createHarness()

    // 目录未返回：不带任何范围参数，列表侧 gate 保持关闭。
    expect(filters.scopeKind).toBeUndefined()
    expect(filters.scopeId).toBeUndefined()
    expect(wrapper.text()).toContain('正在获取')

    resolveCatalog(0, [POOL_SCOPE, SITE_SCOPE])
    await flushPromises()

    expect(scope.hasSelection.value).toBe(true)
    expect(scope.unreadyMessage.value).toBe('')
    expect(filters.scopeKind).toBe('work-pool')
    expect(filters.scopeId).toBe('WMS-SITE-001-RECEIVING')
    expect(filters.skip).toBe(0)

    // 显式切换：范围参数跟着换，并被记住。
    filters.skip = 20
    scope.scopeKey.value = 'site:SITE-001'
    await flushPromises()
    expect(filters.scopeKind).toBe('site')
    expect(filters.scopeId).toBe('SITE-001')
    expect(filters.skip).toBe(0)
    expect(
      localStorage.getItem(
        'nerv-iip.business-console.wms-work-scope.v1:user-admin|org-001|env-dev|receipts',
      ),
    ).toBe('site:SITE-001')
  })

  it('记住的选择仍被授权时优先于目录首项', async () => {
    localStorage.setItem(
      'nerv-iip.business-console.wms-work-scope.v1:user-admin|org-001|env-dev|receipts',
      'site:SITE-001',
    )
    const { filters } = createHarness()

    resolveCatalog(0, [POOL_SCOPE, SITE_SCOPE])
    await flushPromises()

    expect(filters.scopeKind).toBe('site')
    expect(filters.scopeId).toBe('SITE-001')
  })

  it('目录 403 时透传服务端结论，不含糊成「请稍后重试」，也不带范围发查询', async () => {
    const { wrapper, filters, scope } = createHarness()

    sdkState.catalogRequests[0]!.reject({ status: 403, message: 'forbidden' })
    await flushPromises()

    expect(filters.scopeKind).toBeUndefined()
    expect(filters.scopeId).toBeUndefined()
    expect(scope.hasSelection.value).toBe(false)
    expect(wrapper.text()).toContain('取不到已授权的作业范围')
    expect(wrapper.text()).not.toContain('请稍后重试')
  })

  it('目录成功但零授权范围时指向 IAM 配置，绝不说成「暂无数据」', async () => {
    const { wrapper, scope } = createHarness()

    resolveCatalog(0, [])
    await flushPromises()

    expect(scope.hasSelection.value).toBe(false)
    expect(wrapper.text()).toContain('请到 IAM')
    expect(wrapper.text()).not.toContain('暂无数据')
  })

  it('换主体后读新 principal 的记忆，不沿用上一个主体的选择', async () => {
    localStorage.setItem(
      'nerv-iip.business-console.wms-work-scope.v1:user-admin|org-001|env-dev|receipts',
      'site:SITE-001',
    )
    const { filters, auth } = createHarness()

    resolveCatalog(0, [POOL_SCOPE, SITE_SCOPE])
    await flushPromises()
    expect(filters.scopeId).toBe('SITE-001')

    // 换主体：上一个主体的选择即便仍在授权清单里也不能继续沿用——新主体没有记忆，
    // 应回落清单首项。
    auth.principal = { principalId: 'user-emp-049' } as never
    await flushPromises()

    expect(filters.scopeKind).toBe('work-pool')
    expect(filters.scopeId).toBe('WMS-SITE-001-RECEIVING')
  })
})
