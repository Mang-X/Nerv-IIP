import { test } from '@playwright/test'
import {
  STORAGE_KEY,
  session,
  routeConsoleApi,
  routeBusinessConsoleApi,
} from './column-width/fixture'
import { registerDocumentIdentity } from './column-width/documentIdentity'
import { registerDeclaredWidths } from './column-width/declaredWidths'
import { registerDiagnostics } from './column-width/diagnostics'
import { registerPlanning } from './column-width/planning'
import { registerDowntime } from './column-width/downtime'
import { registerTraceAndHandover } from './column-width/traceAndHandover'
import { registerOperationTasks } from './column-width/operationTasks'
import { registerPublicIdentity } from './column-width/publicIdentity'

registerDocumentIdentity()

test.use({ viewport: { width: 1440, height: 900 } })

test.beforeEach(async ({ page }) => {
  await page.addInitScript(
    ({ key, storedSession }) => localStorage.setItem(key, JSON.stringify(storedSession)),
    { key: STORAGE_KEY, storedSession: session },
  )
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

registerDeclaredWidths()

registerDiagnostics()

registerPlanning()

registerDowntime()

// #4469 / #4455 r1：真实浏览器 + Gateway fixture，只证明列表布局与既有交互。
registerTraceAndHandover()

registerOperationTasks()

// #4464：来源为票面缺号反例；fixture 证明展示/布局/请求身份，不冒充真实栈。
registerPublicIdentity()
