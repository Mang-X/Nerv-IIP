import type { Component } from 'vue'
import type { BusinessPermissionCode } from '@/permissions'

/**
 * 表单里的 `DirectoryPicker creatable`：目录类型 → 新增弹窗。
 *
 * 约定：`directory-creators/<目录类型>.ts` 默认导出一个 `DirectoryCreator`，文件名就是
 * `DirectoryPicker` 的 `directory-type`。新接一类只加一个文件，不改选择器。
 *
 * 弹窗约定：
 * - `v-model:open` 控制开关；
 * - 每次点入口，选择器都会重新挂载一个全新的弹窗实例（递增 `key`），所以弹窗在 setup 里按
 *   `context` 初始化表单即可，不用自己写「重新打开时重置」；
 * - 可选的 `context` 属性收调用方给的上下文（`DirectoryPicker` 的 `create-context` 原样传入），
 *   用来预填父级，如设备表单已选产线时新增工位传 `{ lineCode: 'LINE-01' }`；键由各类弹窗自己定义；
 * - 建好后发出 `created`，带上新建项的编码和名称（`DirectoryCreatedItem`），选择器据此自动选中
 *   并显示名称。
 */
export interface DirectoryCreator {
  /** 有这个权限才出现「新增」入口。 */
  permission: BusinessPermissionCode
  /** 新增弹窗；用 `defineAsyncComponent` 包一层，首次点入口时才加载。 */
  dialog: Component
}

/** 调用方给新增弹窗的上下文（父级预填等）。 */
export type DirectoryCreateContext = Record<string, string>

export interface DirectoryCreatedItem {
  code: string
  name: string
}

const creators = import.meta.glob<DirectoryCreator>('./directory-creators/*.ts', {
  eager: true,
  import: 'default',
})

export function directoryCreatorFor(directoryType: string): DirectoryCreator | undefined {
  return creators[`./directory-creators/${directoryType}.ts`]
}
