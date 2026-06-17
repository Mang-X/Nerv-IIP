/**
 * Dashboard — composable shell partition primitives (Nuxt UI Pro style):
 * Group (root row) → Sidebar + Panel(s); each Panel frames a fixed Navbar +
 * Toolbar over a scrolling body. Finer-grained than the all-in-one app shell,
 * for hand-composed control-plane layouts. Token-driven.
 */
export { default as DashboardGroup } from './DashboardGroup.vue'
export { default as DashboardSidebar } from './DashboardSidebar.vue'
export { default as DashboardPanel } from './DashboardPanel.vue'
export { default as DashboardNavbar } from './DashboardNavbar.vue'
export { default as DashboardToolbar } from './DashboardToolbar.vue'
