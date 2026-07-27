/**
 * 抽屉尺寸档位（统一约定，替代调用点手写 `sm:max-w-*`）。
 * 左右抽屉调 `max-width`，上下抽屉调 `max-height`；窄屏一律先占满，
 * 到 `sm` 断点才收窄，避免出现「半截抽屉」。
 *
 * - `sm`   确认类 / 单列表单（24rem）
 * - `md`   默认：详情摘要 + 少量字段（32rem）
 * - `lg`   详情 + 分组信息（42rem）
 * - `xl`   **表格类内容默认档**，放得下 5~7 列（56rem）
 * - `2xl`  宽表格 / 并排双栏（72rem）
 * - `full` 铺满，两侧留 2rem 边距
 */
export type NvSheetSize = 'sm' | 'md' | 'lg' | 'xl' | '2xl' | 'full'

/** 左右抽屉的宽度档位。 */
export const NV_SHEET_INLINE_SIZE: Record<NvSheetSize, string> = {
  sm: 'data-[side=left]:sm:max-w-sm data-[side=right]:sm:max-w-sm',
  md: 'data-[side=left]:sm:max-w-lg data-[side=right]:sm:max-w-lg',
  lg: 'data-[side=left]:sm:max-w-2xl data-[side=right]:sm:max-w-2xl',
  xl: 'data-[side=left]:sm:max-w-4xl data-[side=right]:sm:max-w-4xl',
  '2xl': 'data-[side=left]:sm:max-w-6xl data-[side=right]:sm:max-w-6xl',
  full: 'data-[side=left]:sm:max-w-[calc(100vw-4rem)] data-[side=right]:sm:max-w-[calc(100vw-4rem)]',
}

/** 上下抽屉的高度档位。 */
export const NV_SHEET_BLOCK_SIZE: Record<NvSheetSize, string> = {
  sm: 'data-[side=top]:max-h-72 data-[side=bottom]:max-h-72',
  md: 'data-[side=top]:max-h-112 data-[side=bottom]:max-h-112',
  lg: 'data-[side=top]:max-h-152 data-[side=bottom]:max-h-152',
  xl: 'data-[side=top]:max-h-192 data-[side=bottom]:max-h-192',
  '2xl': 'data-[side=top]:max-h-240 data-[side=bottom]:max-h-240',
  full: 'data-[side=top]:max-h-[calc(100vh-4rem)] data-[side=bottom]:max-h-[calc(100vh-4rem)]',
}

export { default as NvSheetContent } from './NvSheetContent.vue'
export { default as NvSheetTitle } from './NvSheetTitle.vue'
export { default as NvSheetDescription } from './NvSheetDescription.vue'
export { default as NvSheetHeader } from './NvSheetHeader.vue'
export { default as NvSheetFooter } from './NvSheetFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Nv names.
export {
  DialogClose as NvSheetClose,
  DialogRoot as NvSheet,
  DialogTrigger as NvSheetTrigger,
} from 'reka-ui'
