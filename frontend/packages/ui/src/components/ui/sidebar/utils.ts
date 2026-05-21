import { inject, provide } from 'vue'
import type { InjectionKey, Ref } from 'vue'

export const SIDEBAR_WIDTH = '16rem'
export const SIDEBAR_WIDTH_ICON = '3rem'

interface SidebarContext {
  state: Ref<'expanded' | 'collapsed'>
  open: Ref<boolean>
  setOpen: (value: boolean) => void
  toggleSidebar: () => void
  isMobile: Ref<boolean>
}

const SidebarContextKey: InjectionKey<SidebarContext> = Symbol('SidebarContext')

export function provideSidebar(context: SidebarContext) {
  provide(SidebarContextKey, context)
}

export function useSidebar(): SidebarContext {
  const ctx = inject(SidebarContextKey)
  if (!ctx) throw new Error('useSidebar must be used within SidebarProvider')
  return ctx
}
