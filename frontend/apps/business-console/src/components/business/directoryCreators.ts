import type { Component } from 'vue'
import type { BusinessPermissionCode } from '@/permissions'

/**
 * 表单里的 `DirectoryPicker creatable`：目录类型 → 新增弹窗。
 *
 * 约定：`directory-creators/<目录类型>.ts` 默认导出一个 `DirectoryCreator`，文件名就是
 * `DirectoryPicker` 的 `directory-type`。新接一类只加一个文件，不改选择器。
 *
 * 弹窗约定：`v-model:open` 控制开关；建好后发出 `created`，带上新建项的编码和名称
 * （`DirectoryCreatedItem`），选择器据此自动选中并显示名称。
 */
export interface DirectoryCreator {
  /** 有这个权限才出现「新增」入口。 */
  permission: BusinessPermissionCode
  /** 新增弹窗；用 `defineAsyncComponent` 包一层，点入口时才加载。 */
  dialog: Component
}

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
