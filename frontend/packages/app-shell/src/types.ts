import type { Component } from 'vue'
import type { RouteLocationRaw } from 'vue-router'

/** Signed-in user shown in the shell user menu. */
export interface ShellUser {
  name: string
  email?: string
}

/**
 * Top-level capability area (the horizontal "top" of the T). Only domains the
 * current user may see should be passed in — the shell does no enforcement.
 */
export interface NavDomain {
  id: string
  title: string
  icon?: Component
  /** Landing route when the domain is selected. */
  to?: RouteLocationRaw
  /** Permission codes; the consumer filters before passing in (UX only). */
  requiredPermissions?: string[]
}

/** A side-nav leaf linking to a module/page (the left of the T). */
export interface NavLink {
  title: string
  to: RouteLocationRaw
  icon?: Component
  requiredPermissions?: string[]
}

/** Optional labelled grouping inside a domain's side navigation. */
export interface NavGroup {
  label?: string
  items: NavLink[]
}

/** Domain-local side navigation — groups of links for the current domain. */
export type SideNav = NavGroup[]

/** How top domains that don't fit are surfaced. `more` = trailing dropdown. */
export type OverflowStrategy = 'more'
