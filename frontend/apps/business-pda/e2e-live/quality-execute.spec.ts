import type { APIRequestContext, Page } from '@playwright/test'
import { expect, request as playwrightRequest, test } from '@playwright/test'
import { loginViaUi } from './support/login'
import { assertLiveStackReachable } from './support/preflight'
import { simulateScanGun } from './support/scan-gun'

// L2 真实栈仿真走查——quality 链路真实写路径 spec（方案文档 §4.2 末条 / §4.5 / §8 M1c）。
// 无任何 page.route mock：真实 IAM 登录 → 选待检任务 → 逐特性录合格结果 → 真实提交
// （POST /quality/inspection-tasks/{id}/inspection-record）→ 幂等语义捕获（请求体/头无显式
// 幂等键 + 同请求重放返回同一 inspectionRecordId）→ 直连 BusinessGateway 回读任务状态
// pending→completed、且回链 inspectionRecordId 与提交响应一致（「写落一条事实」）。
//
// 只走**合格路径**（全部特性录 in-spec/合格值）：不合格会触发后端同事务自动开 NCR
// （CreateInspectionRecordFromTaskCommand.cs `EnsureNcrAndBuildResultAsync`），live 走查
// 不制造 NCR 脏数据。
//
// 幂等键代码事实结论（详见下方断言处注释）：该链路**无显式幂等键**，靠任务生命周期守门
// （first-write-wins keyed on task）——
// - 前端 `src/composables/useBusinessQualityInspectionTasks.ts` L65-67 注释 + L200-212
//   submitInspection：body 仅 { inspectorUserId, resultLines, dispositionReason? }，无键字段；
// - 契约 `packages/api-client/.../types.gen.ts` L1405-1410
//   BusinessConsoleCreateInspectionRecordFromTaskRequest 四字段，无幂等键；
// - 后端 `backend/services/Business/Quality/.../Commands/InspectionTasks/
//   CreateInspectionRecordFromTaskCommand.cs` L52-58：任务已 completed → 回读既有记录返回
//   同一 inspectionRecordId（幂等重放 no-op）。

/** 与 support/preflight.ts 同源的网关基址（vite.config.ts 代理 target 同一 env 变量）。 */
const BUSINESS_GATEWAY_BASE = process.env.NERV_IIP_BUSINESS_GATEWAY_URL ?? 'http://127.0.0.1:5119'

/** 提交端点路径片段（代码事实：api-client sdk.gen.ts L407
 * `POST /api/business-console/v1/quality/inspection-tasks/{inspectionTaskId}/inspection-record`）。 */
const SUBMIT_PATH_RE = /\/quality\/inspection-tasks\/([^/?]+)\/inspection-record/

/** 检验计划特性 GET 路径（sdk.gen.ts L417
 * `/quality/inspection-plans/{inspectionPlanId}/characteristics`）。 */
const CHARACTERISTICS_PATH_RE = /\/quality\/inspection-plans\/[^/]+\/characteristics/

// ---- 与 api-client types.gen.ts 同形的最小响应形状（只取本 spec 断言用字段）----

interface PlanCharacteristicItem {
  characteristicCode?: string
  name?: string
  /** 'attribute' → 计数特性，否则计量（代码事实：useInspectionExecution.ts `kindOfCharacteristic`）。 */
  characteristicType?: string
  required?: boolean
  lowerSpecLimit?: number | null
  upperSpecLimit?: number | null
  unitCode?: string | null
}

interface InspectionTaskItem {
  inspectionTaskId?: string
  skuCode?: string
  status?: string
  inspectionRecordId?: string | null
}

interface Envelope<T> {
  success?: boolean
  message?: string | null
  data?: T | null
}

interface SubmitResultData {
  inspectionRecordId?: string | null
  result?: string
  nonconformanceReportId?: string | null
  nonconformanceReportCode?: string | null
}

/**
 * 在计量特性的规格区间内挑一个**可用数字键盘录入**的合格值。
 * 判定口径（代码事实：business-core `inspections/qualityResults.ts`
 * `characteristicRowOutOfTolerance` L80-89）：**边界含**——仅 `< lower` 或 `> upper` 才超差，
 * 故直接取下限（或仅有上限时取上限）必为合格；两限皆空取 1。
 * NvNumberKeyboard 只有 0-9 与 '.'（NumberKeyboard.vue，measuredValue 场景 extraKey='.'，
 * QualityExecuteStep.vue `keyboardExtraKey`），无法录负数——负值区间如实报环境阻塞。
 */
function pickInSpecValue(lower: number | null | undefined, upper: number | null | undefined) {
  let value: number
  if (lower != null) value = lower
  else if (upper != null) value = upper
  else value = 1
  if (value < 0) {
    if ((lower == null || lower <= 0) && (upper == null || upper >= 0)) {
      value = 0
    } else {
      throw new Error(
        `环境阻塞：计量特性规格区间 [${lower ?? '-∞'}, ${upper ?? '+∞'}] 为纯负值区间，` +
          '数字键盘（0-9 与小数点）无法录入负数合格值。请调整 seed 计划特性后重跑。',
      )
    }
  }
  const text = String(value)
  if (!/^\d+(?:\.\d+)?$/.test(text)) {
    throw new Error(
      `环境阻塞：规格下限/上限 ${text} 无法用数字键盘逐键录入（科学计数法等）。请调整 seed 后重跑。`,
    )
  }
  return text
}

/**
 * 用屏上 NvNumberKeyboard 逐键录入数值并点「完成」收起。
 * 选择器代码事实：面板 `[data-slot="number-keyboard"]`、数字/小数点为面板内同名按钮、
 * 「完成」为 confirmText 默认值（ui-mobile NumberKeyboard.vue L27/L64/L79-127；
 * 头部与右下各有一个「完成」，取 first）。
 */
async function typeOnNumberKeyboard(page: Page, text: string) {
  const panel = page.locator('[data-slot="number-keyboard"]')
  await expect(panel).toBeVisible()
  for (const ch of text) {
    await panel.getByRole('button', { name: ch, exact: true }).click()
  }
  await panel.getByRole('button', { name: '完成' }).first().click()
  await expect(panel).toHaveCount(0)
}

/**
 * 直连 BusinessGateway 回读任务事实：GET /quality/inspection-tasks?status=completed
 * （代码事实：sdk.gen.ts L401；后端 ListInspectionTasksQuery.cs L52-56 status 为小写字符串过滤，
 * "completed" 合法值见同文件 L66 排序表达式；skuCode 精确过滤 L58-62；take 上限 200 见
 * 验证器 L40）。按 take=200 受限翻页查找目标任务，不把 take 扩过上限
 * （同 useBusinessQualityInspectionTasks.ts MAX_TAKE 口径）。
 */
async function readBackCompletedTask(
  api: APIRequestContext,
  authorization: string,
  organizationId: string,
  environmentId: string,
  inspectionTaskId: string,
  skuCode: string | undefined,
): Promise<InspectionTaskItem | null> {
  const take = 200
  for (let skip = 0; skip < 10_000; skip += take) {
    const response = await api.get(
      `${BUSINESS_GATEWAY_BASE}/api/business-console/v1/quality/inspection-tasks`,
      {
        headers: { authorization },
        params: {
          organizationId,
          environmentId,
          status: 'completed',
          ...(skuCode ? { skuCode } : {}),
          skip,
          take,
        },
      },
    )
    expect(response.ok(), `后端回读失败：HTTP ${response.status()}`).toBe(true)
    const envelope = (await response.json()) as Envelope<{ items?: InspectionTaskItem[] }>
    expect(envelope.success, `后端回读信封失败：${envelope.message ?? ''}`).toBe(true)
    const items = envelope.data?.items ?? []
    const hit = items.find((t) => t.inspectionTaskId === inspectionTaskId)
    if (hit) return hit
    if (items.length < take) return null
  }
  return null
}

test('live 写路径：真实登录 → 选待检任务 → 录合格结果提交 → 幂等语义捕获 → 后端状态回读', async ({
  page,
}) => {
  // 真实栈全链路（列表加载 + 计划特性 + 提交 + 回读），放宽单测超时。
  test.setTimeout(180_000)

  await assertLiveStackReachable()
  await loginViaUi(page)

  // 收集列表 GET 响应（提交后按 inspectionTaskId 反查 skuCode，用于回读窄化过滤）。
  const listItems: InspectionTaskItem[] = []
  page.on('response', (response) => {
    const url = response.url()
    if (
      response.request().method() !== 'GET' ||
      !url.includes('/quality/inspection-tasks') ||
      SUBMIT_PATH_RE.test(url)
    ) {
      return
    }
    void response
      .json()
      .then((envelope: Envelope<{ items?: InspectionTaskItem[] }>) => {
        listItems.push(...(envelope.data?.items ?? []))
      })
      .catch(() => {})
  })

  await page.goto('/quality/tasks')
  await expect(page.getByRole('heading', { name: '检验任务' })).toBeVisible()
  await expect(page.getByText('加载中…')).toHaveCount(0, { timeout: 15_000 })
  await expect(page.getByTestId('tasks-error')).toHaveCount(0)

  // ---- 步骤 2 进入执行步：优先 env 扫码直达，缺省点击首行 ----
  // 选择器代码事实：task-row = QualityTaskListView.vue L91 NvListRow data-testid；
  // 步骤指示「第 2/3 步」= tasks.vue L61/L112-114；扫码输入框 = M1b quality-tasks.spec.ts 同款
  // （QualityTaskScanFilter 的 ScanBar placeholder）。
  //
  // 计划特性 GET 等待器必须在**触发动作之前**注册：扫码直达 / 点击首行本身就会选中任务并
  // 触发懒加载（tasks.vue L47-53），本地栈响应快时后注册会漏捕 → 20s 超时假失败。
  // 两个分支各自「先建 promise（不 await）→ 触发动作 → 事后 await」。
  //
  // 创建后立即在**派生 promise** 上挂 no-op catch（`promise.catch(...)` 返回新 promise，
  // 不吞原 promise 的拒绝语义——后续 `await charResponsePromise` 仍会拿到原始拒绝）：
  // simulateScanGun/click/步骤断言先失败时，这个 20s 等待器会在测试失败后才延迟拒绝，
  // 若无人收编就成 unhandled rejection，掩盖真实首因。两个分支统一由此处收编。
  const waitForCharacteristicsResponse = () => {
    const responsePromise = page.waitForResponse(
      (r) => r.request().method() === 'GET' && CHARACTERISTICS_PATH_RE.test(r.url()),
      { timeout: 20_000 },
    )
    void responsePromise.catch(() => {})
    return responsePromise
  }
  let charResponsePromise: ReturnType<typeof waitForCharacteristicsResponse>
  const executeCode = process.env.NERV_IIP_LIVE_EXECUTE_CODE
  if (executeCode) {
    const scanInput = page.locator('input[placeholder^="扫来源单据"]')
    await expect(scanInput).toBeFocused()
    // 注册在扫码前：唯一命中即直达执行步并触发计划特性 GET。
    charResponsePromise = waitForCharacteristicsResponse()
    await simulateScanGun(page, executeCode)
    // 写路径要求全局唯一命中直达执行步；退化为筛选说明 env 码不唯一/未命中 → 如实失败
    // （未兑现的特性等待器已在创建处统一收编，不会产生 unhandled rejection）。
    try {
      await expect(page.getByText('第 2/3 步')).toBeVisible({ timeout: 15_000 })
    } catch {
      throw new Error(
        `环境阻塞：NERV_IIP_LIVE_EXECUTE_CODE=${executeCode} 未全局唯一命中待检任务` +
          '（退化为关键字筛选）。写路径需要唯一命中直达执行步，请换用唯一来源单号或移除该 env 走首行路径。',
      )
    }
  } else {
    const rows = page.getByTestId('task-row')
    if ((await rows.count()) === 0) {
      throw new Error(
        '环境阻塞：待检任务列表为空——请先 seed 待检任务（QualitySeedService）' +
          '或用 NERV_IIP_LIVE_EXECUTE_CODE 指定唯一来源单号。live 走查不伪造数据、不静默跳过。',
      )
    }
    // 注册在点击前：选中任务即触发计划特性 GET（tasks.vue L47-53 懒加载），避免竞态漏捕。
    charResponsePromise = waitForCharacteristicsResponse()
    await rows.first().click()
    await expect(page.getByText('第 2/3 步')).toBeVisible()
  }

  // ---- 步骤 3 按计划特性填写合格值直到提交解禁 ----
  // 计划特性 GET（真实读）：等待响应并取权威上下限（比解析 DOM 的 spec-range 文案更可靠）。
  const charResponse = await charResponsePromise
  const charEnvelope = (await charResponse.json()) as Envelope<{
    items?: PlanCharacteristicItem[]
  }>
  expect(charEnvelope.success).toBe(true)
  const characteristics = (charEnvelope.data?.items ?? []).filter((c) => c.characteristicCode)
  if (characteristics.length === 0) {
    throw new Error(
      '环境阻塞：该检验计划未配置检验特性（后端返回空 items），无法走写路径——' +
        '请先 seed 带特性的检验计划（QualitySeedService）。',
    )
  }

  // 必检特性会自动补齐为行（useInspectionExecution.ts L109-118 watch immediate）；
  // 「全部添加」把剩余非必检特性也加入（QualityExecuteStep.vue L186-194
  // data-testid=add-all-characteristics，availableCharacteristics 为空时禁用）。
  const addAll = page.getByTestId('add-all-characteristics')
  await expect(addAll).toBeVisible({ timeout: 15_000 })
  if (await addAll.isEnabled()) {
    await addAll.click()
  }
  const charRows = page.getByTestId('char-row')
  await expect(charRows).toHaveCount(characteristics.length)

  // 行 ↔ 响应项**按索引**关联（不用展示名匹配：后端只保证 characteristicCode 唯一，
  // name 可重名）。行序代码事实：useInspectionExecution.ts L110-118 watch immediate 先按
  // 响应顺序补齐全部**必检**特性行；「全部添加」→ addAllCharacteristics（同文件 L99-101）
  // 再按响应顺序追加剩余非必检行（addCharacteristic 按 characteristicCode 跳过已加入的）；
  // QualityExecuteStep.vue L144-153 v-for 按 rows 数组序渲染。
  // 故 DOM 行序 = 必检（响应序）+ 非必检（响应序）。
  const orderedCharacteristics = [
    ...characteristics.filter((c) => c.required),
    ...characteristics.filter((c) => !c.required),
  ]

  // 逐行录**合格**值。行内选择器代码事实：QualityCharacteristicRow.vue——
  // char-name L41 / measured-value L60 / count-pass L86 / out-of-tolerance L77。
  for (let i = 0; i < orderedCharacteristics.length; i += 1) {
    const row = charRows.nth(i)
    const item = orderedCharacteristics[i]
    // 展示名交叉校验（非关联依据）：若上述行序推导与实现漂移，在此就地失败而非录错行。
    // 文案代码事实：char-name = `row.name || row.characteristicCode`（QualityCharacteristicRow.vue
    // L41-43），row.name = c.name ?? c.characteristicCode ?? ''（useInspectionExecution.ts L86）。
    await expect(row.getByTestId('char-name')).toHaveText(
      item.name || item.characteristicCode || '',
    )
    if (item.characteristicType === 'attribute') {
      // 计数特性：直接判「合格」（合格路径不选不合格，避免原因码/不良数与 NCR）。
      await row.getByTestId('count-pass').click()
    } else {
      // 计量特性：数字键盘录 in-spec 值（含边界）→ 断言无超差红警示。
      const value = pickInSpecValue(item.lowerSpecLimit, item.upperSpecLimit)
      await row.getByTestId('measured-value').click()
      await typeOnNumberKeyboard(page, value)
      await expect(row.getByTestId('measured-value')).toContainText(value)
      await expect(row.getByTestId('out-of-tolerance')).toHaveCount(0)
    }
  }

  // 提交解禁断言：合格路径 verdict=pass → 无处置原因输入、按钮文案「提交检验结果」且 enabled
  // （QualityDispositionSubmit.vue L18/L27-38：dispositionRequired 仅 fail 时出现）。
  const submitButton = page.getByTestId('submit')
  await expect(submitButton).toHaveText('提交检验结果')
  await expect(submitButton).toBeEnabled()
  await expect(page.getByTestId('missing-required')).toHaveCount(0)

  // ---- 提交 + 请求捕获（先挂 waitForResponse 再点击，杜绝竞态）----
  const submitResponsePromise = page.waitForResponse(
    (r) => r.request().method() === 'POST' && SUBMIT_PATH_RE.test(r.url()),
    { timeout: 30_000 },
  )
  await submitButton.click()
  const submitResponse = await submitResponsePromise
  const submitRequest = submitResponse.request()
  const submitUrl = new URL(submitRequest.url())

  expect(submitResponse.status()).toBe(200)
  const inspectionTaskId = SUBMIT_PATH_RE.exec(submitUrl.pathname)?.[1]
  expect(inspectionTaskId, `提交 URL 未含任务 id：${submitUrl.pathname}`).toBeTruthy()
  const organizationId = submitUrl.searchParams.get('organizationId')
  const environmentId = submitUrl.searchParams.get('environmentId')
  expect(organizationId).toBeTruthy()
  expect(environmentId).toBeTruthy()

  // 幂等键代码事实断言：**无显式幂等键，靠任务生命周期守门**。
  // 请求体字段只可能是 { inspectorUserId, resultLines, dispositionReason, dispositionAttachmentFileIds }
  // （契约 types.gen.ts L1405-1410；前端实际只发前三者，useBusinessQualityInspectionTasks.ts
  // L200-212），请求头亦无 Idempotency-Key 类头。幂等由后端按任务状态守门：
  // CreateInspectionRecordFromTaskCommand.cs L52-58 已完成任务回读既有记录。
  const submitBody = submitRequest.postDataJSON() as Record<string, unknown>
  const allowedBodyKeys = new Set([
    'inspectorUserId',
    'resultLines',
    'dispositionReason',
    'dispositionAttachmentFileIds',
  ])
  for (const key of Object.keys(submitBody)) {
    expect(allowedBodyKeys.has(key), `请求体出现契约外字段：${key}`).toBe(true)
    expect(key.toLowerCase()).not.toContain('idempotency')
  }
  const submitHeaders = await submitRequest.allHeaders()
  for (const headerName of Object.keys(submitHeaders)) {
    expect(headerName.toLowerCase()).not.toContain('idempotency')
  }
  const authorization = submitHeaders.authorization
  // 会话 token 从提交请求的 Authorization 头捕获——localStorage 只持久化
  // { principal, refreshToken, sessionId }，**不含 accessToken**
  // （packages/auth/src/store.ts persistSession L224-235），故不能从 storage 读。
  expect(authorization, '提交请求缺少 Authorization 头').toMatch(/^Bearer .+/)

  const submitEnvelope = (await submitResponse.json()) as Envelope<SubmitResultData>
  expect(submitEnvelope.success, `提交信封失败：${submitEnvelope.message ?? ''}`).toBe(true)
  const inspectionRecordId = submitEnvelope.data?.inspectionRecordId
  expect(inspectionRecordId, '提交响应缺少 inspectionRecordId').toBeTruthy()
  // 全部特性录 in-spec/合格 → 后端权威结论应为 passed（口径：QualityResultStep.vue L9-33）。
  expect(submitEnvelope.data?.result).toBe('passed')
  expect(submitEnvelope.data?.nonconformanceReportId ?? null).toBeNull()

  // ---- 结果页断言（选择器代码事实：QualityResultStep.vue L24-27「检验合格」标题、
  // L36「检验结果已记录。」、L72 next-task 按钮）----
  await expect(page.getByText('检验合格')).toBeVisible()
  await expect(page.getByText('检验结果已记录。')).toBeVisible()
  await expect(page.getByTestId('next-task')).toBeVisible()
  await expect(page.getByTestId('ncr-link')).toHaveCount(0)

  // ---- 幂等重放 + 后端状态回读（直连 BusinessGateway，绕过 vite 代理）----
  const api = await playwrightRequest.newContext()
  try {
    // 幂等重放：同 URL/body/token 原样重放一次——任务已 completed，后端返回**同一**
    // inspectionRecordId（CreateInspectionRecordFromTaskCommand.cs L52-58 first-write-wins），
    // 即「回读只落一条事实」的写侧证明（方案文档 §4.2 末条，本链路无键版本）。
    const replayResponse = await api.post(
      `${BUSINESS_GATEWAY_BASE}${submitUrl.pathname}${submitUrl.search}`,
      { headers: { authorization }, data: submitBody },
    )
    expect(replayResponse.status()).toBe(200)
    const replayEnvelope = (await replayResponse.json()) as Envelope<SubmitResultData>
    expect(replayEnvelope.success).toBe(true)
    expect(replayEnvelope.data?.inspectionRecordId).toBe(inspectionRecordId)
    expect(replayEnvelope.data?.result).toBe('passed')
    // 合格路径重放不得补开 NCR。代码事实：CreateInspectionRecordFromTaskCommand.cs 的完成重放
    // 路径（L52-58）仍走统一收尾 EnsureNcrAndBuildResultAsync（L130-141）——对 **rejected**
    // 既有记录，重放会补开缺失的 NCR 并回链（副作用分支）。该分支属未来「不合格路径」spec；
    // 本 spec 强制合格路径规避 NCR 脏数据，故重放响应必须保持 NCR 为空。
    expect(replayEnvelope.data?.nonconformanceReportId ?? null).toBeNull()

    // 状态回读：status=completed 列表中找到该任务，且回链 inspectionRecordId 与提交响应一致。
    const skuCode = listItems.find((t) => t.inspectionTaskId === inspectionTaskId)?.skuCode
    const completedTask = await readBackCompletedTask(
      api,
      authorization,
      organizationId as string,
      environmentId as string,
      inspectionTaskId as string,
      skuCode,
    )
    expect(
      completedTask,
      `后端回读未在 status=completed 列表中找到任务 ${inspectionTaskId}——写未落库或状态未翻转。`,
    ).not.toBeNull()
    expect(completedTask?.status).toBe('completed')
    expect(completedTask?.inspectionRecordId).toBe(inspectionRecordId)
  } finally {
    await api.dispose()
  }
})
