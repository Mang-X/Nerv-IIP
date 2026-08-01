import { expect, test } from '@playwright/test'
import { loginViaUi } from './support/login'
import { assertLiveStackReachable } from './support/preflight'
import { simulateScanGun } from './support/scan-gun'

// L2 真实栈仿真走查——quality 链路只读 smoke（方案文档 §8 M1b）。
// 无任何 page.route mock：真实 IAM 登录 → 真实 BusinessGateway 待检任务列表 → S1 常驻直扫。
// 只读约束：本 spec 不做任何写操作，扫码只更新服务端 keyword 筛选。写路径归 M1c。

test('live 只读链路：真实登录 → /quality/tasks 渲染 → S1 常驻直扫触发服务端筛选', async ({
  page,
}) => {
  await assertLiveStackReachable()
  await loginViaUi(page)

  await page.goto('/quality/tasks')
  await expect(page.getByRole('heading', { name: '检验任务' })).toBeVisible()

  // 等待真实列表请求落定（loading 消失），并断言非错误态（RetryableListError 不出现）。
  await expect(page.getByText('加载中…')).toHaveCount(0)
  await expect(page.getByTestId('tasks-error')).toHaveCount(0)

  // S1 前提断言：ScanBar 挂载后焦点常驻（不做任何点击/聚焦操作）。
  const scanInput = page.getByPlaceholder('扫描或输入来源单据 / SKU 以筛选')
  await expect(scanInput).toBeVisible()
  await expect(scanInput).toBeFocused()

  // 扫码值：优先 env 注入；缺省时从第一行任务读取来源单号（真实数据驱动，不伪造）。
  let code = process.env.NERV_IIP_LIVE_SCAN_CODE
  if (!code) {
    const rows = page.getByTestId('task-row')
    if ((await rows.count()) === 0) {
      throw new Error(
        '环境阻塞：待检任务列表为空且未提供 NERV_IIP_LIVE_SCAN_CODE——' +
          '请先 seed 待检任务（QualitySeedService）或用 NERV_IIP_LIVE_SCAN_CODE 指定条码。' +
          'live 走查不伪造数据、不静默跳过。',
      )
    }
    const firstRowText = await rows.first().innerText()
    const match = /来源单\s+([^\s·]+)/.exec(firstRowText)
    if (!match) {
      throw new Error(
        `无法从首行任务提取来源单号（行文本：${firstRowText.replaceAll('\n', ' | ')}）。` +
          '请用 NERV_IIP_LIVE_SCAN_CODE 显式指定扫码值。',
      )
    }
    code = match[1]
  }

  // S1 常驻直扫：不 focus、不 fill，DOM 层键盘楔入近似（突发字符流 + Enter 后缀）。
  await simulateScanGun(page, code)

  // 扫码值只作为服务端 keyword 条件；操作员仍需等待列表并显式选行。
  await expect(page.getByText(`筛选：${code}`)).toBeVisible()
  await expect(page.getByText('第 2/3 步')).toHaveCount(0)
})
