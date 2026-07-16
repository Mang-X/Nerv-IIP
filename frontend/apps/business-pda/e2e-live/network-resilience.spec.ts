import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { loginViaUi } from './support/login'
import { applySlowNetwork } from './support/network'
import { assertLiveStackReachable } from './support/preflight'

// L2 真实栈仿真走查——M2 网络/超时韧性 spec（方案文档 §4.2 / §8 M2，真机差异点清单第 5 条）。
// 真实 IAM 登录 + 真实 BusinessGateway 数据加载完成后，再注入网络故障（断网 / 请求挂起 /
// CDP 节流）——不 mock 任何业务数据，注入的只是**传输层故障**（方案 §4.2 明示的
// `page.route 故障注入`，与 M1「无 page.route 数据 mock」原则不冲突）。
//
// 全部场景**只读**：载体为 /quality/tasks 列表 + 选中任务后的检验计划特性 GET
// （`/quality/inspection-plans/{id}/characteristics`，tasks.vue L46-53 选中即懒加载，
// useBusinessQualityInspectionTasks.ts L240-258 enabled 按 planId 键控——**每次选中必发
// 一次真实 GET**，是不依赖额外 seed 数据形状的确定性请求触发器）。不进执行表单提交路径。
//
// 错误透出面（代码事实）：
// - 面板：QualityExecuteStep.vue L155-163 `RetryableListError`（test-id
//   `plan-characteristics-error`）→ RetryableListError.vue L24 经 `describeRequestError`
//   取**类型化错误的 message 原文**展示 → ErrorRetry.vue L27/L36-40 面板 role=alert +
//   `retry-list` 重试按钮。
// - 离线文案：`OfflineError`「当前离线，请检查网络连接后重试」
//   （src/api/request-timeout.ts L41-46；预检 L104-106 按 navigator.onLine 在 dispatch
//   前抛出，`context.setOffline(true)` 恰好翻转该探针）。
// - 超时文案：`RequestTimeoutError`「网络超时，请检查连接后重试」
//   （src/api/request-timeout.ts L33-38）。
// - loading 文案：「加载检验计划特性中…」（QualityExecuteStep.vue L164-169）。
//
// 【场景 4「headers 已到、body 卡死」——live 层不实现，调查结论如实声明】
// Playwright 的 `route.fulfill()` 是**原子**下发：只接受完整 body（string/Buffer/json/path
// 五种互斥来源），headers 与 body 一次性交付，没有「先发 headers、body 流保持打开」的
// 流式/分片 API——该形态无法用路由注入仿真（route.continue({ url }) 重定向到本地 stall
// server 属另一机制，引入真实 HTTP server 与跨端口 CORS/协议约束，未验证，不在本里程碑硬造）。
// 该形态已由 L0 集成测试覆盖（真实 api-client 组合，非 mock 单元）：
// - src/api/request-timeout.integration.test.ts L81-112「surfaces a RequestTimeoutError when
//   a real facade RESPONSE BODY stalls after headers」（headersOkBodyStalls L27-41：headers
//   立即 200、text() 挂起直到 abort → 超时文案）；
// - src/api/download-timeout.integration.test.ts L19-47：SOP 下载 blob body 卡死，经
//   openDownloadGrantBlob 自身 ceiling abort → 「网络超时」。
// live 层不重复造该形态（覆盖归属声明同步在 docs/architecture/mobile-pda-testing-and-smoke.md L2 节）。

/** 检验计划特性 GET 路径（代码事实：api-client sdk.gen.ts
 * `/quality/inspection-plans/{inspectionPlanId}/characteristics`，同 M1c spec 口径）。 */
const CHARACTERISTICS_PATH_RE = /\/quality\/inspection-plans\/[^/]+\/characteristics/

/** 场景 2 对「数秒内透出」的硬上限：注入超时不得超过它（否则退化为长等，违背不真等 30s）。 */
const MAX_INJECTED_TIMEOUT_MS = 5_000

/**
 * 进入 /quality/tasks 并确保列表已真实加载且**有行可选**（选中行是本 spec 的请求触发器）。
 * 空列表按 M1 口径 throw 环境阻塞——live 走查不伪造数据、不静默跳过。
 */
async function openTasksListWithRows(page: Page): Promise<void> {
  await page.goto('/quality/tasks')
  await expect(page.getByRole('heading', { name: '检验任务' })).toBeVisible()
  await expect(page.getByText('加载中…')).toHaveCount(0, { timeout: 30_000 })
  await expect(page.getByTestId('tasks-error')).toHaveCount(0)
  if ((await page.getByTestId('task-row').count()) === 0) {
    throw new Error(
      '环境阻塞：待检任务列表为空——网络韧性场景需要选中一行以触发检验计划特性 GET。' +
        '请先 seed 待检任务（QualitySeedService）。live 走查不伪造数据、不静默跳过。',
    )
  }
}

/** 选中首行进入执行步（tasks.vue L59-61 步骤指示），随即触发计划特性真实 GET。 */
async function selectFirstTask(page: Page): Promise<void> {
  await page.getByTestId('task-row').first().click()
  await expect(page.getByText('第 2/3 步')).toBeVisible()
}

/**
 * 「特性数据已真实落定」锚点——按 QualityExecuteStep.vue 加载成功后的三种合法数据形态取
 * 三选一锚点（三者随数据形态互斥/共存，`.first()` 收敛 Playwright 严格模式）：
 *  1. `char-row`：计划含**必检**特性——useInspectionExecution.ts 的 watch 只自动补齐
 *     required 特性行，必检行到位即渲染 QualityCharacteristicRow；
 *  2. `no-plan-characteristics`：计划为空（planCharacteristics.length === 0 的权威空态，
 *     QualityExecuteStep.vue）；
 *  3. `add-all-characteristics`：计划**非空但全部 optional**——此时不自动建行（无 char-row）、
 *     也非空计划（无空态），但加载成功的 v-else 分支必然渲染「全部添加」按钮
 *     （QualityExecuteStep.vue data-testid="add-all-characteristics"，disabled 也可见）。
 */
function characteristicsSettledLocator(page: Page) {
  return page
    .getByTestId('char-row')
    .or(page.getByTestId('no-plan-characteristics'))
    .or(page.getByTestId('add-all-characteristics'))
    .first()
}

/**
 * 故障解除后的恢复断言：以「特性数据真实落定」为最终锚点（见
 * characteristicsSettledLocator 的三形态说明），且错误面板/loading 均消失。
 *
 * 容错点击：Pinia Colada 默认 `refetchOnReconnect: true`（node_modules @pinia/colada
 * dist 查询默认值），恢复联网触发 online 事件时错误查询可能**自动重取自愈**，
 * 重试按钮在点击瞬间可能已被移除——点击失败不算错，最终以内容可见为准。
 */
async function expectCharacteristicsRecovered(page: Page): Promise<void> {
  const retryButton = page.getByTestId('plan-characteristics-error').getByTestId('retry-list')
  if (await retryButton.isVisible().catch(() => false)) {
    // 必须带短超时：isVisible 与 click 之间 refetchOnReconnect 自愈会移除按钮，
    // 不带 timeout 的 click 会按 actionability 重试等按钮重现、吃满整个测试预算
    // （live 实跑 trace 实证悬挂 106s 直至 120s 测试超时）；点击失败本就不算错。
    await retryButton.click({ timeout: 2_000 }).catch(() => {})
  }
  await expect(characteristicsSettledLocator(page)).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('plan-characteristics-error')).toHaveCount(0)
  await expect(page.getByText('加载检验计划特性中…')).toHaveCount(0)
}

test('live 网络韧性：离线预检——类型化离线文案透出（非白屏非裸堆栈），恢复后可重载', async ({
  page,
  context,
}) => {
  test.setTimeout(120_000)
  await assertLiveStackReachable()
  await loginViaUi(page)
  await openTasksListWithRows(page)

  // 断网。保真声明（方案 §4.2）：只仿真 navigator.onLine=false（Chromium 网络仿真同步翻转
  // 该探针，正中 request-timeout.ts L104-106 的离线预检），不代表 Wi-Fi 抖动/DNS/TLS/
  // captive portal。注入在页面模块与列表数据**已在线加载完成之后**——选中行不做路由跳转
  // （tasks.vue 同 chunk 内 v-if 切步），离线时不会撞上 vite 模块加载失败的干扰项。
  await context.setOffline(true)
  try {
    await selectFirstTask(page)

    const errorPanel = page.getByTestId('plan-characteristics-error')
    await expect(errorPanel).toBeVisible({ timeout: 15_000 })
    // 类型化文案原文（OfflineError 默认 message，request-timeout.ts L41-46）。
    await expect(errorPanel).toContainText('当前离线，请检查网络连接后重试')
    // 非白屏：页面骨架（header 标题）仍在；非裸堆栈：面板不透出 Error 类名/堆栈。
    await expect(page.getByRole('heading', { name: '检验任务' })).toBeVisible()
    await expect(errorPanel).not.toContainText('OfflineError')
    // 离线预检在 dispatch 前抛出（请求未离开设备）→ 读页保留安全重试（#814 口径），
    // 面板必须提供重试按钮。
    await expect(errorPanel.getByTestId('retry-list')).toBeVisible()
  } finally {
    await context.setOffline(false)
  }

  // 恢复联网 → 重试（或 refetchOnReconnect 自愈）→ 特性内容真实加载。
  await expectCharacteristicsRecovered(page)
})

test('live 网络韧性：请求整体挂起 + 短超时注入——超时文案数秒内透出（不真等 30s），释放后可重试', async ({
  page,
}) => {
  test.setTimeout(120_000)

  // 依赖声明：本场景假定 main.ts 已支持 `VITE_NERV_IIP_REQUEST_TIMEOUT_MS` 超时注入
  // （读 env 传给 createTimeoutFetch({ timeoutMs })，与本 spec 同一 PR 的并行改动）。
  // webServer 继承进程环境变量（playwright.live.config.ts webServer 注释），故运行时在
  // 命令行注入即可：`VITE_NERV_IIP_REQUEST_TIMEOUT_MS=2000 pnpm e2e:live`
  // （pda-live-walkthrough.ps1 的默认自起 server 路径已默认注入 2000）。
  // 未注入/注入过长时如实报环境阻塞——绝不退化成真等 30s 的假「数秒」断言。
  // 解析口径与 app 侧 resolveRequestTimeoutMs 对齐（request-timeout.ts：仅 DEV 生效——
  // live webServer 是 vite dev、DEV=true 故覆盖通道可用；只认纯正整数字符串且钳制区间
  // [100, 30000]，其余一律回落 30s 默认）——spec 侧若放宽会出现「spec 以为注入了 2s、
  // app 实跑 30s」的错位，故低于 100ms 的注入值这里同样按环境阻塞拒绝。
  const rawInjected = (process.env.VITE_NERV_IIP_REQUEST_TIMEOUT_MS ?? '').trim()
  const injectedTimeoutMs = /^\d+$/.test(rawInjected) ? Number(rawInjected) : Number.NaN
  if (!Number.isSafeInteger(injectedTimeoutMs) || injectedTimeoutMs < 100) {
    throw new Error(
      '环境阻塞：未注入（或注入值非纯正整数 / 低于 app 侧钳制下限 100ms、会被 app 回落 30s ' +
        '默认的）短超时——本场景要求设 VITE_NERV_IIP_REQUEST_TIMEOUT_MS（推荐 2000）后运行 ' +
        'pnpm e2e:live（webServer 继承进程 env）。' +
        '不注入则超时为默认 30s，「数秒内透出」断言无法诚实成立，故直接失败而非等 30s。',
    )
  }
  if (injectedTimeoutMs > MAX_INJECTED_TIMEOUT_MS) {
    throw new Error(
      `环境阻塞：注入超时 ${injectedTimeoutMs}ms 超过本场景上限 ${MAX_INJECTED_TIMEOUT_MS}ms——` +
        '请设更短值（推荐 2000）。',
    )
  }

  await assertLiveStackReachable()
  await loginViaUi(page)
  await openTasksListWithRows(page)

  // 故障注入：对计划特性 GET 挂起不响应（不 fulfill、不 continue、不 abort——请求悬挂，
  // 等 app 侧 AbortController 到点自行中止）。谓词按 URL pathname 匹配，只拦此一路径。
  const isCharacteristicsUrl = (url: URL) => CHARACTERISTICS_PATH_RE.test(url.pathname)
  await page.route(isCharacteristicsUrl, () => {
    // 有意悬挂：请求既不放行也不响应，复现「请求整体挂起」形态。
  })

  const startedAt = Date.now()
  await selectFirstTask(page)

  const errorPanel = page.getByTestId('plan-characteristics-error')
  // 「数秒内」上限 = 注入超时 + 渲染余量，硬性远小于默认 30s。
  await expect(errorPanel).toBeVisible({ timeout: injectedTimeoutMs + 8_000 })
  // 类型化文案原文（RequestTimeoutError 默认 message，request-timeout.ts L33-38）。
  await expect(errorPanel).toContainText('网络超时，请检查连接后重试')
  await expect(errorPanel).not.toContainText('RequestTimeoutError')
  const elapsedMs = Date.now() - startedAt
  expect(
    elapsedMs,
    `超时文案透出耗时 ${elapsedMs}ms，应受注入超时（${injectedTimeoutMs}ms）约束而非默认 30s`,
  ).toBeLessThan(injectedTimeoutMs + 10_000)

  // 释放路由 → 重试 → 特性真实加载（悬挂中的旧请求已被 app 超时 abort，不影响新请求）。
  await page.unroute(isCharacteristicsUrl)
  await expectCharacteristicsRecovered(page)
})

test('live 网络韧性：慢网（CDP 节流）——loading 态呈现、不闪断、同 URL 不重复请求', async ({
  page,
}) => {
  test.setTimeout(120_000)
  await assertLiveStackReachable()
  await loginViaUi(page)
  await openTasksListWithRows(page)

  // 同 URL 请求计数：慢网下 loading 若「闪断重发」，会表现为第二次特性 GET——计数即证伪。
  let characteristicsRequests = 0
  page.on('request', (request) => {
    if (request.method() === 'GET' && CHARACTERISTICS_PATH_RE.test(request.url())) {
      characteristicsRequests += 1
    }
  })

  // CDP 节流（Network.emulateNetworkConditions 已 deprecated，适配层隔离见 support/network.ts；
  // 仅 Chromium）。节流在页面加载完成后才施加，只作用于特性 GET 这次真实请求。
  const restoreNetwork = await applySlowNetwork(page)
  try {
    await selectFirstTask(page)

    // loading 态呈现：600ms 附加时延给出 ≥1.2s 可观测窗口（QualityExecuteStep.vue L164-169）。
    const loading = page.getByText('加载检验计划特性中…')
    await expect(loading).toBeVisible()

    // 慢网最终成功落定（非错误态）：特性数据落定锚点（三形态说明见
    // characteristicsSettledLocator）可见，loading 消失、无错误面板。
    await expect(characteristicsSettledLocator(page)).toBeVisible({ timeout: 30_000 })
    await expect(loading).toHaveCount(0)
    await expect(page.getByTestId('plan-characteristics-error')).toHaveCount(0)

    // 不重复请求：整个慢网窗口内特性 GET 恰好一次。
    expect(characteristicsRequests, '慢网下同 URL 不得重复请求（loading 不闪断重发）').toBe(1)
  } finally {
    await restoreNetwork()
  }
})
