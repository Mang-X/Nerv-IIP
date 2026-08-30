import { DialogClose, DialogRoot, DialogTrigger } from 'reka-ui'

export { default as NvDialogContent } from './NvDialogContent.vue'
export { default as NvDialogTitle } from './NvDialogTitle.vue'
export { default as NvDialogDescription } from './NvDialogDescription.vue'
export { default as NvDialogHeader } from './NvDialogHeader.vue'
export { default as NvDialogFooter } from './NvDialogFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Nv names.
export const NvDialogClose = /* @__PURE__ */ Object.assign({}, DialogClose, {
  name: 'NvDialogClose',
}) as typeof DialogClose
export const NvDialog = /* @__PURE__ */ Object.assign({}, DialogRoot, {
  name: 'NvDialog',
}) as typeof DialogRoot
export const NvDialogTrigger = /* @__PURE__ */ Object.assign({}, DialogTrigger, {
  name: 'NvDialogTrigger',
}) as typeof DialogTrigger
