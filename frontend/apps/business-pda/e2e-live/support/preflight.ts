import { request } from '@playwright/test'

/**
 * 真实栈可达性预检。
 *
 * 探测两个网关的匿名 `GET /health`（代码事实：
 * `backend/gateway/BusinessGateway/.../Endpoints/Health/HealthEndpoint.cs` 与
 * `backend/gateway/PlatformGateway/.../Endpoints/Health/HealthEndpoint.cs`，均
 * `[AllowAnonymous]`，返回 text/plain "Healthy" 或 "Degraded: ..."）。
 *
 * 基址与 `vite.config.ts` 代理同源：`NERV_IIP_BUSINESS_GATEWAY_URL`（默认 5119）、
 * `NERV_IIP_PLATFORM_GATEWAY_URL`（默认 5100）。
 *
 * 不可达时 **throw**——绝不 `test.skip` 静默跳过、绝不假绿；环境阻塞必须如实失败。
 */
export async function assertLiveStackReachable(): Promise<void> {
  const targets = [
    {
      name: 'BusinessGateway',
      url: `${process.env.NERV_IIP_BUSINESS_GATEWAY_URL ?? 'http://127.0.0.1:5119'}/health`,
    },
    {
      name: 'PlatformGateway',
      url: `${process.env.NERV_IIP_PLATFORM_GATEWAY_URL ?? 'http://127.0.0.1:5100'}/health`,
    },
  ]

  const api = await request.newContext()
  try {
    for (const target of targets) {
      let failure: string | null = null
      try {
        const response = await api.get(target.url, { timeout: 5_000 })
        if (!response.ok()) {
          failure = `HTTP ${response.status()}`
        }
      } catch (e) {
        failure = e instanceof Error ? e.message : String(e)
      }
      if (failure) {
        throw new Error(
          `环境阻塞：真实栈不可用——${target.name} 健康探测失败（${target.url} → ${failure}）。` +
            `请先在仓库根目录用 nerv.ps1 dev 拉起完整栈（Docker 先开），再运行 pnpm e2e:live。` +
            `live 走查不降级、不跳过：栈不可用时如实报告阻塞。`,
        )
      }
    }
  } finally {
    await api.dispose()
  }
}
