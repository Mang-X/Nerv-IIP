import {
  test,
  principal,
  seedStoredSession,
  routeConsoleApi,
  routeBusinessConsoleApi,
} from './console-scenarios/fixture'
import { registerEntry } from './console-scenarios/entry'
import { registerMesNavigation } from './console-scenarios/mesNavigation'
import { registerOrdersAndMaterials } from './console-scenarios/ordersAndMaterials'
import { registerOperationAndReporting } from './console-scenarios/operationAndReporting'

test.beforeEach(async ({ page, permissionCodes }) => {
  const currentPrincipal = { ...principal, permissionCodes }
  await seedStoredSession(page, currentPrincipal)
  await page.route('**/api/console/v1/**', (route) => routeConsoleApi(route, currentPrincipal))
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

registerEntry()

registerMesNavigation()

registerOrdersAndMaterials()

registerOperationAndReporting()
