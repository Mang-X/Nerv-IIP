import { TooltipProvider, TooltipRoot, TooltipTrigger } from 'reka-ui'

export { default as NvTooltipContent } from './NvTooltipContent.vue'
// Provider / root / trigger carry no styling — re-export reka under Nv names.
export const NvTooltipProvider = /* @__PURE__ */ Object.assign({}, TooltipProvider, {
  name: 'NvTooltipProvider',
}) as typeof TooltipProvider
export const NvTooltip = /* @__PURE__ */ Object.assign({}, TooltipRoot, {
  name: 'NvTooltip',
}) as typeof TooltipRoot
export const NvTooltipTrigger = /* @__PURE__ */ Object.assign({}, TooltipTrigger, {
  name: 'NvTooltipTrigger',
}) as typeof TooltipTrigger
