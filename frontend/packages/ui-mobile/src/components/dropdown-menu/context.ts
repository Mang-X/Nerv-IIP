import type { InjectionKey, Ref } from 'vue'

/**
 * Shared state for the filter bar: which item id is currently open (only one at
 * a time) and a toggle that closes any other open item.
 */
export interface DropdownMenuContext {
  openId: Ref<string | null>
  toggle: (id: string) => void
  close: () => void
}

export const dropdownMenuKey: InjectionKey<DropdownMenuContext> = Symbol('mobile-dropdown-menu')
