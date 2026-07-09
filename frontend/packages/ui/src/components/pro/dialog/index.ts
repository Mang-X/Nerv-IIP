export {
  default as NvDialogContent,
  /** @deprecated Use `NvDialogContent` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as DialogProContent,
} from './DialogProContent.vue'
export {
  default as NvDialogTitle,
  /** @deprecated Use `NvDialogTitle` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as DialogProTitle,
} from './DialogProTitle.vue'
export {
  default as NvDialogDescription,
  /** @deprecated Use `NvDialogDescription` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as DialogProDescription,
} from './DialogProDescription.vue'
export {
  default as NvDialogHeader,
  /** @deprecated Use `NvDialogHeader` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as DialogProHeader,
} from './DialogProHeader.vue'
export {
  default as NvDialogFooter,
  /** @deprecated Use `NvDialogFooter` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as DialogProFooter,
} from './DialogProFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Nv names.
export {
  DialogClose as NvDialogClose,
  DialogRoot as NvDialog,
  DialogTrigger as NvDialogTrigger,
  /** @deprecated Use `NvDialogClose` (ADR 0020 NvUI); alias removed after codemod #789. */
  DialogClose as DialogProClose,
  /** @deprecated Use `NvDialog` (ADR 0020 NvUI); alias removed after codemod #789. */
  DialogRoot as DialogPro,
  /** @deprecated Use `NvDialogTrigger` (ADR 0020 NvUI); alias removed after codemod #789. */
  DialogTrigger as DialogProTrigger,
} from 'reka-ui'
