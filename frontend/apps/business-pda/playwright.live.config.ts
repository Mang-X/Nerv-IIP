import { defineConfig, devices } from '@playwright/test'

// L2 真实栈仿真走查（live）配置——与全 mock 的 `playwright.config.ts` 刻意分离：
// - 无任何 `page.route` mock，vite dev 双代理直连真实网关
//   （`/api/business-console`→BusinessGateway 5119、`/api/console`→PlatformGateway 5100，
//   可用 NERV_IIP_BUSINESS_GATEWAY_URL / NERV_IIP_PLATFORM_GATEWAY_URL 覆盖，
//   webServer 继承进程环境变量、由 vite.config.ts 读取）。
// - 真实后端共享 seed/admin/组织环境，禁止并行（workers: 1）。
// - 显式命令运行（`pnpm e2e:live`），不进 CI（同 *PostgresProfileTests 环境门控口径）。
// 方案文档：frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md §4 / §8 M1b。

const executablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
// 独立端口 5177：避开 PDA dev(5126) 与全 mock e2e(5176)。
const port = Number(process.env.PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT ?? 5177)
const baseURL = `http://127.0.0.1:${port}`

export default defineConfig({
  testDir: './e2e-live',
  outputDir: './test-results-live',
  forbidOnly: true,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: 'list',
  use: {
    baseURL,
    launchOptions: executablePath ? { executablePath } : undefined,
    // 证据包口径（方案文档 §4.5）：trace 全程开启，截图仅失败时补充。
    trace: 'on',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: `vp dev --host 127.0.0.1 --port ${port}`,
    url: baseURL,
    // 默认**不复用**已有 server：5177 上若是另一 worktree 的 vite，会测到别人的代码却把
    // 当前 worktree 的 commit SHA 写进证据（证据失真）。确需与人工调试共用同一 dev server
    // 时显式设 PLAYWRIGHT_PDA_LIVE_REUSE=1（此时 worktree 归属由使用者自行担保，
    // pda-live-walkthrough.ps1 -AllowServerReuse 会在证据 metadata 里如实记录）。
    reuseExistingServer: process.env.PLAYWRIGHT_PDA_LIVE_REUSE === '1',
    timeout: 120_000,
  },
  projects: [
    {
      name: 'mobile-live',
      use: { ...devices['Pixel 5'], viewport: { width: 390, height: 844 } },
    },
  ],
})
