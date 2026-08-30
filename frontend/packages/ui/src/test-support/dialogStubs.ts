/**
 * `NvDialog` / `NvSheet` 是 reka-ui `DialogRoot` 的**重命名再导出**
 * （`components/pc/dialog/index.ts`、`components/pc/sheet/index.ts` 都写
 * `DialogRoot as NvDialog` / `DialogRoot as NvSheet`）。它们和
 * `NvDialogClose`/`NvSheetClose`（`DialogClose`）、
 * `NvDialogTrigger`/`NvSheetTrigger`（`DialogTrigger`）同源。
 *
 * Vue `<script setup>` 在编译期把模板里的组件标签直接绑定到导入的局部变量
 * （`_component_NvDialog = __unref(NvDialog)`），**不会**像非 setup 模板那样
 * 经运行时 `resolveComponent(tag)` 按字符串查找。`@vue/test-utils` 的
 * `global.stubs` 是按被解析组件对象的 `__name`（脚本 setup SFC 的编译期
 * 内部名）匹配的，而不是按导入方给它起的别名——所以 `stubs: { NvDialog: ... }`
 * 永远匹配不上，测试会静默挂载真实的 `DialogRoot`（无条件渲染 slot），
 * 让「弹框关闭时不渲染内容」一类断言失效方向为**假绿**。
 *
 * 这份映射按 reka-ui 组件的真实 `__name` 建键，因此对 `NvDialog` 和 `NvSheet`
 * 这两个不同的 Nv 别名同时生效（两者都是同一个 `DialogRoot`）。用法：
 *
 * ```ts
 * import { dialogStubs } from '@nerv-iip/ui/test-support'
 *
 * mount(Consumer, { global: { stubs: dialogStubs } })
 * ```
 */
export const dialogStubs = {
  DialogRoot: true,
  DialogClose: true,
  DialogTrigger: true,
} as const
