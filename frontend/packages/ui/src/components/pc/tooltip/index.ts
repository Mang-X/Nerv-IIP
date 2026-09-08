import { TooltipProvider, TooltipRoot, TooltipTrigger } from 'reka-ui'

export { default as NvTooltipContent } from './NvTooltipContent.vue'
// Provider / root / trigger carry no styling；浅拷贝补 `name` 的理由见 `../dialog/index.ts`。
export const NvTooltipProvider = /* @__PURE__ */ Object.assign({}, TooltipProvider, {
  name: 'NvTooltipProvider',
})
export const NvTooltip = /* @__PURE__ */ Object.assign({}, TooltipRoot, {
  name: 'NvTooltip',
})
export const NvTooltipTrigger = /* @__PURE__ */ Object.assign({}, TooltipTrigger, {
  name: 'NvTooltipTrigger',
})
