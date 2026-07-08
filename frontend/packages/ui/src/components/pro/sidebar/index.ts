// Pro — premium sidebar pieces that compose with the base `Sidebar*` primitives
// (which now carry the unified premium selected state from theme.css). These add
// the workspace brand lockup, status dots, the animated submenu and the user
// footer — the polish previously trapped in the Sidebar doc demo.
export {
  default as NvSidebarBrand,
  /** @deprecated Use `NvSidebarBrand` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as SidebarProBrand,
} from './SidebarProBrand.vue'
export {
  default as NvSidebarDot,
  /** @deprecated Use `NvSidebarDot` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as SidebarProDot,
} from './SidebarProDot.vue'
export {
  default as NvSidebarSub,
  /** @deprecated Use `NvSidebarSub` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as SidebarProSub,
} from './SidebarProSub.vue'
export {
  default as NvSidebarUser,
  /** @deprecated Use `NvSidebarUser` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as SidebarProUser,
} from './SidebarProUser.vue'
