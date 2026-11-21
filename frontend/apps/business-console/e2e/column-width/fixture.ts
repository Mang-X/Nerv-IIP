import { type Page, expect, type Route } from '@playwright/test'

// #3734：NvDataTable 的列宽声明此前落在 <th> 上、表格却是默认 table-layout: auto，
// 于是声明被内容挤掉（完工入库页〈入库状态〉声明 w-48 实测 64px，徽标被截成一个字）。
//
// 这条断言必须在真实浏览器里跑：jsdom 不实现表格布局、也不加载 SFC 样式表，用
// getComputedStyle 在单测里量列宽只会得到假绿（组件单测钉的是「声明落到了表头上」，
// 量不到「声明宽 == 渲染宽」）。CI 浏览器不变量这条 lane 见 ci.yml 的
// "Test Business Console browser invariants"，本 spec 挂在同一 job 上。

export const STORAGE_KEY = 'nerv-iip.business-console.auth'

export const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  // 逐页对应两页的 meta.requiredPermissions（缺一个码整页跳 /forbidden）：
  // /mes/receipts 与 /wms/inbound 都取 business.mes.receipts.read /
  // business.wms.receipts.read。
  permissionCodes: ['business.mes.receipts.read', 'business.wms.receipts.read'],
}

export const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

/**
 * 逐列断言「声明宽 == 渲染宽」，容差 ±1px（subpixel 舍入）。
 *
 * 同时核对声明是以哪种写法落到表头上的：`w-*` 应当只走 class，CSS 尺寸应当只走
 * inline style。只量宽度不核对落地形式的话，组件把某列悄悄换成另一种写法也能过，
 * 那正是「两种写法都覆盖」要防的。
 */
export async function assertDeclaredWidthsHold(
  page: Page,
  expected: { header: string; expectedPx: number; style: 'class' | 'dimension' }[],
) {
  for (const column of expected) {
    const head = page.locator('table.nv-dt-table thead th', { hasText: column.header })
    await expect(head, `列头未渲染：${column.header}`).toBeVisible()
    const measured = await head.evaluate((element) => ({
      width: element.getBoundingClientRect().width,
      inlineWidth: element.style.width,
      hasWidthClass: Array.from(element.classList).some((name) => name.startsWith('w-')),
    }))
    expect(
      Math.abs(measured.width - column.expectedPx),
      `列「${column.header}」声明 ${column.expectedPx}px，实际渲染 ${measured.width.toFixed(1)}px`,
    ).toBeLessThanOrEqual(1)
    if (column.style === 'class') {
      expect(measured.hasWidthClass, `列「${column.header}」应走 w-* class 写法`).toBe(true)
      expect(measured.inlineWidth, `列「${column.header}」不该同时挂 inline width`).toBe('')
    } else {
      expect(measured.inlineWidth, `列「${column.header}」应走 CSS 尺寸 inline style`).not.toBe('')
      expect(measured.hasWidthClass, `列「${column.header}」不该同时挂 w-* class`).toBe(false)
    }
  }
}

export async function statusColumnIndex(page: Page) {
  const heads = page.locator('table.nv-dt-table thead th')
  return heads.evaluateAll((elements) =>
    elements.findIndex((element) => element.textContent?.trim() === '入库状态'),
  )
}

export async function routeConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  if (url.pathname === '/api/console/v1/auth/refresh') {
    return route.fulfill({ json: { success: true, data: session } })
  }
  if (url.pathname === '/api/console/v1/auth/me') {
    return route.fulfill({ json: { success: true, data: principal } })
  }
  return route.fulfill({ json: { success: true, data: {} } })
}

export async function routeBusinessConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())

  if (pathname === '/api/business-console/v1/me/work-context') {
    return route.fulfill({ json: { success: true, data: { organizationId: 'org-001' } } })
  }

  // 完工入库：状态徽标要放得下完整文案，否则量的是被裁切后的宽度。
  if (pathname === '/api/business-console/v1/mes/finished-goods-receipt-requests') {
    return route.fulfill({
      json: {
        success: true,
        data: {
          items: [
            {
              receiptRequestId: 'receipt-1',
              workOrderNo: 'WO-001',
              requestNo: 'RCV-2026-0001',
              skuCode: 'SKU-001',
              quantity: 5,
              unitCost: 1280,
              // 取 api-client 生成类型 receiptStatus 联合类型里的真值（页面按它查标签表）：
              // `Posted` 才是「已入库」（`completed` 属工单状态域，入库语境不查这张表）。
              // #3912：契约收敛到 FinishedGoodsReceiptRequest 的真实值域后运行时是 PascalCase；
              // 旧 fixture 写的小写 `posted` 之所以还能过，是因为词表有一层大小写归一化把它吸收了。
              receiptStatus: 'Posted',
              requestedAtUtc: '2026-09-22T10:31:00.000Z',
            },
          ],
        },
      },
    })
  }

  if (pathname === '/api/business-console/v1/wms/inbound-orders') {
    return route.fulfill({
      json: {
        success: true,
        data: {
          items: [
            {
              inboundOrderId: 'inbound-1',
              inboundOrderNo: 'PO-2026-0001',
              status: 'Pending',
              quality: '合格 5 / 拒收 0',
              createdAtUtc: '2026-09-22T10:31:00.000Z',
            },
          ],
        },
      },
    })
  }

  return route.fulfill({ json: { success: true, data: {} } })
}
