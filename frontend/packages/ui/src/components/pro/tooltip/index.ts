export {
  default as NvTooltipContent,
  /** @deprecated Use `NvTooltipContent` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as TooltipProContent,
} from './TooltipProContent.vue'
// Provider / root / trigger carry no styling — re-export reka under Nv names.
export {
  TooltipProvider as NvTooltipProvider,
  TooltipRoot as NvTooltip,
  TooltipTrigger as NvTooltipTrigger,
  /** @deprecated Use `NvTooltipProvider` (ADR 0020 NvUI); alias removed after codemod #789. */
  TooltipProvider as TooltipProProvider,
  /** @deprecated Use `NvTooltip` (ADR 0020 NvUI); alias removed after codemod #789. */
  TooltipRoot as TooltipPro,
  /** @deprecated Use `NvTooltipTrigger` (ADR 0020 NvUI); alias removed after codemod #789. */
  TooltipTrigger as TooltipProTrigger,
} from 'reka-ui'
