// Legacy two-level sidebar shell (kept for migration compatibility).
export { default as AppShell } from './AppShell.vue'
export type { NavItem, NavSubItem } from './AppShell.vue'

// T-shaped navigation shell (top capability domains + domain-local side nav).
export { default as AppShellT } from './AppShellT.vue'
export { default as NavSide } from './NavSide.vue'
export { default as NavTopDomains } from './NavTopDomains.vue'
export type {
  NavDomain,
  NavGroup,
  NavLink,
  OverflowStrategy,
  ShellUser,
  SideNav,
} from './types'
