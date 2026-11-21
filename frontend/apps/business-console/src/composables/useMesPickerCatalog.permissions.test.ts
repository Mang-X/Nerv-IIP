import { configureApiClient } from '@nerv-iip/api-client'
import { PiniaColada } from '@pinia/colada'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it, vi } from 'vitest'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessSkus } from './useBusinessMasterData'
import { useEngineeringProductionVersions } from './engineering'

it('目录读取失败后换到无权岗位，旧错误消失且刷新不会再发请求', async () => {
  const fetch = vi.fn(async () => Response.json({ message: '目录暂时不可用' }, { status: 503 }))
  configureApiClient({ baseUrl: 'http://gateway.test', fetch })
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.principal = {
    principalId: 'planner',
    permissionCodes: [P.masterDataProductsRead, P.engineeringProductionVersionsRead],
  }
  useBusinessContextStore(pinia).patchContext({ organizationId: 'org', environmentId: 'env' })
  let skus!: ReturnType<typeof useBusinessSkus>
  let versions!: ReturnType<typeof useEngineeringProductionVersions>
  const view = mount(
    {
      setup() {
        skus = useBusinessSkus()
        versions = useEngineeringProductionVersions(
          () =>
            auth.principal?.permissionCodes?.includes(P.engineeringProductionVersionsRead) === true,
        )
        return () => null
      },
    },
    { global: { plugins: [pinia, PiniaColada] } },
  )
  try {
    await vi.waitFor(() => {
      expect(skus.skusError.value).toBeTruthy()
      expect(versions.productionVersionsError.value).toBeTruthy()
    })
    const beforeSwitch = fetch.mock.calls.length
    auth.principal = { principalId: 'operator', permissionCodes: [P.mesFoundationRead] }
    expect(skus.skusError.value).toBeNull()
    expect(versions.productionVersionsError.value).toBeNull()
    await Promise.all([skus.refreshSkus(), versions.refresh()])
    expect(fetch).toHaveBeenCalledTimes(beforeSwitch)
  } finally {
    view.unmount()
    configureApiClient({})
  }
})
