import { DialogClose, DialogRoot, DialogTrigger } from 'reka-ui'

export { default as NvDialogContent } from './NvDialogContent.vue'
export { default as NvDialogTitle } from './NvDialogTitle.vue'
export { default as NvDialogDescription } from './NvDialogDescription.vue'
export { default as NvDialogHeader } from './NvDialogHeader.vue'
export { default as NvDialogFooter } from './NvDialogFooter.vue'
// Root / trigger / close carry no styling. 必须浅拷贝一份再补 `name`：Vue 按
// `name || __name` 解析组件身份，而 reka 产物只有 `__name`（reka 真名）；且 sheet
// 把同一个 `DialogRoot` 又导出成 `NvSheet`，原地改名会互相覆盖。
export const NvDialogClose = /* @__PURE__ */ Object.assign({}, DialogClose, {
  name: 'NvDialogClose',
})
export const NvDialog = /* @__PURE__ */ Object.assign({}, DialogRoot, {
  name: 'NvDialog',
})
export const NvDialogTrigger = /* @__PURE__ */ Object.assign({}, DialogTrigger, {
  name: 'NvDialogTrigger',
})
