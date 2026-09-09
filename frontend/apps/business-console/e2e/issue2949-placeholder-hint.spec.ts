import { mkdir } from 'node:fs/promises'
import path from 'node:path'
import { expect, test, type Page, type Route } from '@playwright/test'

import { requireBrowserEvidenceOutputDir } from '../playwright.config'
import { BUSINESS_PERMISSION_CODES } from '../src/permissions'

/**
 * #2949 真机走查：半栏栅格里的示例文本从 placeholder 迁到 NvFieldDescription 之后，
 * 表单在**真实浏览器**里的排版要成立。
 *
 * 为什么必须是真浏览器：本改动新增 6 条可见的 hint 行，会改变半栏表单的行高、换行
 * 与分组密度；jsdom 没有布局引擎，量不出任何宽度，`dist/assets` 里出现字符串也只
 * 证明它进了 bundle，不证明它在半栏里显示得下。视口取 1440×900 —— 与 #2706 抓到
 * 「…如 DEV」截断时的走查条件一致。
 *
 * 截断判据（票面点名的正确做法）：**量 placeholder 文本的渲染宽度与输入框内容宽度作比较**。
 * 用 canvas `measureText` 按控件自己的计算字体量文本，再比 `clientWidth` 减去左右
 * padding 与边框后的内容宽。
 *
 * **不使用 `input.scrollWidth === clientWidth`** —— 空值未聚焦的 `<input>`，placeholder
 * 根本不计入 `scrollWidth`，该等式恒成立，是 #2706 已实证的假绿。
 *
 * hint 是会换行的 `<p>`，所以对它量的是「有没有出现横向溢出」(`scrollWidth <= clientWidth`)，
 * 这对块级文本是有效读数。
 *
 * 跑法：
 *   NERV_IIP_OUT_DIR=<产物目录> pnpm exec playwright test e2e/issue2949 --project=desktop
 * （playwright.config 的 webServer 会自行拉起 `vp dev`；不需要后端栈，读面用 page.route 造。）
 */

const STORAGE_KEY = 'nerv-iip.business-console.auth'

const principal = {
  principalId: 'principal-2949',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  // 走查要点开每个页面的新建表单，权限不足会把触发按钮整个 v-if 掉。
  permissionCodes: Object.values(BUSINESS_PERMISSION_CODES),
}

test.use({ viewport: { width: 1440, height: 900 } })

test.beforeEach(async ({ page }) => {
  await page.addInitScript(
    ({ key, storedSession }) => localStorage.setItem(key, JSON.stringify(storedSession)),
    {
      key: STORAGE_KEY,
      storedSession: { principal, refreshToken: 'refresh-token', sessionId: 'session-2949' },
    },
  )
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

const session = {
  accessToken: 'access-token-2949',
  refreshToken: 'refresh-token',
  sessionId: 'session-2949',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

function envelope<T>(data: T) {
  return { success: true, data }
}

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
}

async function routeConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())
  if (pathname === '/api/console/v1/auth/refresh') return fulfillJson(route, envelope(session))
  if (pathname === '/api/console/v1/auth/me') return fulfillJson(route, envelope(principal))
  return fulfillJson(route, envelope({}))
}

/** 读面一律回空信封：本走查只看表单排版，不看列表数据。 */
async function routeBusinessConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  if (url.pathname === '/api/business-console/v1/me/work-context') {
    const scope = { kind: 'organization', id: 'org-001', displayName: '一号工厂' }
    return fulfillJson(
      route,
      envelope({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolvedAtUtc: '2026-09-09T01:00:00.000Z',
        principal: { id: principal.principalId, principalType: principal.principalType },
        resolutionStatus: 'resolved',
        authorizedScopes: [scope],
        availableScopeKinds: ['organization'],
        selectedScope: scope,
        issues: [],
      }),
    )
  }
  return fulfillJson(route, envelope({ items: [], total: 0 }))
}

/** placeholder 文本渲染宽 vs 输入框内容宽；ratio < 1 表示放得下。 */
async function measurePlaceholder(page: Page, selector: string) {
  return page.$eval(selector, (el) => {
    const input = el as HTMLInputElement
    const text = input.placeholder
    const style = getComputedStyle(input)
    const canvas = document.createElement('canvas')
    const ctx = canvas.getContext('2d')!
    ctx.font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`
    const textWidth = ctx.measureText(text).width
    const contentWidth =
      input.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight)
    return {
      text,
      chars: [...text].length,
      textWidth: Math.round(textWidth * 10) / 10,
      contentWidth: Math.round(contentWidth * 10) / 10,
      fits: textWidth <= contentWidth,
    }
  })
}

/** hint 是会换行的 <p>：量横向溢出与实际占用高度。 */
async function measureHint(page: Page, text: string) {
  return page.$eval(`[data-slot="nv-field-description"]:has-text("${text}")`, (el) => {
    const p = el as HTMLParagraphElement
    const rect = p.getBoundingClientRect()
    return {
      text: (p.textContent ?? '').trim(),
      visible: rect.width > 0 && rect.height > 0,
      overflowsX: p.scrollWidth > p.clientWidth,
      width: Math.round(rect.width),
      height: Math.round(rect.height),
      lines: Math.round(rect.height / parseFloat(getComputedStyle(p).lineHeight)),
    }
  })
}

const results: Record<string, unknown>[] = []

async function capture(
  page: Page,
  outDir: string,
  name: string,
  opts: { input?: string; hint?: string; container: string },
) {
  const shot = path.join(outDir, `s1-${name}.png`)
  await page.locator(opts.container).first().screenshot({ path: shot })
  const hint = opts.hint ? await measureHint(page, opts.hint) : null
  const placeholder = opts.input ? await measurePlaceholder(page, opts.input) : null
  results.push({ name, hint, placeholder })
  if (hint) {
    expect(hint.visible, `${name}: hint 必须可见`).toBe(true)
    expect(hint.overflowsX, `${name}: hint 不得横向溢出`).toBe(false)
  }
  if (placeholder) {
    expect(
      placeholder.fits,
      `${name}: placeholder 必须放得下 (${JSON.stringify(placeholder)})`,
    ).toBe(true)
  }
}

test('#2949 半栏栅格 hint 迁移后真实排版核验', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop', '半栏栅格问题只在 desktop 视口成立')
  const outDir = requireBrowserEvidenceOutputDir()
  await mkdir(outDir, { recursive: true })
  // dev server 首次编译每页数秒；给有界但充裕的值，别用默认 0（=永不超时）。
  page.setDefaultTimeout(30_000)
  page.setDefaultNavigationTimeout(90_000)

  const dialog = '[data-slot="dialog-content"], [role="dialog"]'

  // 1. 维护工单（#2706 抓到「…如 DEV」的原始现场）
  await page.goto('/maintenance/work-orders', { waitUntil: 'domcontentloaded' })
  await page.getByRole('button', { name: '新建维护工单' }).click()
  await expect(page.getByLabel('设备', { exact: true })).toBeVisible()
  await capture(page, outDir, '01-maintenance-work-orders', {
    input: '#mwo-device',
    hint: '也可直接输入设备编号',
    container: '[data-slot="nv-sheet-content"]',
  })
  await page.keyboard.press('Escape')

  // 2. 保养计划
  await page.goto('/maintenance/plans', { waitUntil: 'domcontentloaded' })
  await page.getByRole('button', { name: '新建保养计划' }).click()
  await expect(page.getByRole('heading', { name: '新建保养计划' })).toBeVisible()
  await capture(page, outDir, '02-maintenance-plans', {
    input: '#plan-code',
    hint: '可自定义计划编号',
    container: dialog,
  })
  await page.keyboard.press('Escape')

  // 3. 标准工序
  await page.goto('/engineering/standard-operations', { waitUntil: 'domcontentloaded' })
  await page.getByRole('button', { name: '新建工序' }).click()
  await capture(page, outDir, '03-standard-operations', {
    input: '#op-control',
    hint: 'INHOUSE',
    container: dialog,
  })
  await page.keyboard.press('Escape')

  // 4. 技能目录
  await page.goto('/master-data/skill-catalog', { waitUntil: 'domcontentloaded' })
  await page.getByRole('button', { name: '新建技能' }).click()
  await capture(page, outDir, '04-skill-catalog', {
    hint: '例如：机加工',
    container: dialog,
  })
  await page.keyboard.press('Escape')

  // 5. 工程文档：内容类型以 application/pdf 预填，长 placeholder 删掉后不另加说明行，
  //    这里核的是「删掉之后这一格没塌、值仍在」。
  await page.goto('/engineering/documents', { waitUntil: 'domcontentloaded' })
  await page.getByRole('button', { name: '登记文档' }).click()
  await expect(page.locator('#doc-content-type')).toHaveValue('application/pdf')
  await capture(page, outDir, '05-engineering-documents', { container: dialog })
  await page.keyboard.press('Escape')

  // 6/7. EBOM / MBOM 修订号
  for (const [name, route] of [
    ['06-ebom', '/engineering/ebom'],
    ['07-mbom', '/engineering/mbom'],
  ] as const) {
    await page.goto(route, { waitUntil: 'domcontentloaded' })
    await page.getByRole('button', { name: '发布新版本' }).click()
    await capture(page, outDir, name, {
      hint: '例如 A、B、001',
      container: dialog,
    })
    await page.keyboard.press('Escape')
  }

  await page.screenshot({ path: path.join(outDir, 's1-99-final.png'), fullPage: false })
  // eslint-disable-next-line no-console
  console.log('\n=== #2949 真机读数 ===\n' + JSON.stringify(results, null, 2))
})
