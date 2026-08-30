import { SelectGroup, SelectValue } from 'reka-ui'

export { default as NvSelect } from './NvSelect.vue'
export { default as NvSelectTrigger } from './NvSelectTrigger.vue'
export { default as NvSelectContent } from './NvSelectContent.vue'
export { default as NvSelectItem } from './NvSelectItem.vue'
// Unstyled context helpers re-exported under Nv names for ergonomics.
export const NvSelectGroup = /* @__PURE__ */ Object.assign({}, SelectGroup, {
  name: 'NvSelectGroup',
}) as typeof SelectGroup
export const NvSelectValue = /* @__PURE__ */ Object.assign({}, SelectValue, {
  name: 'NvSelectValue',
}) as typeof SelectValue
