import type { InjectionKey, Ref } from 'vue'

export const dialogScrollableKey: InjectionKey<Readonly<Ref<boolean>>> =
  Symbol('nv-dialog-scrollable')
