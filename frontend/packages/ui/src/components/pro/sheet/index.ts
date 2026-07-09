export {
  default as NvSheetContent,
  /** @deprecated Use `NvSheetContent` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as SheetProContent,
} from './SheetProContent.vue'
export {
  default as NvSheetTitle,
  /** @deprecated Use `NvSheetTitle` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as SheetProTitle,
} from './SheetProTitle.vue'
export {
  default as NvSheetDescription,
  /** @deprecated Use `NvSheetDescription` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as SheetProDescription,
} from './SheetProDescription.vue'
export {
  default as NvSheetHeader,
  /** @deprecated Use `NvSheetHeader` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as SheetProHeader,
} from './SheetProHeader.vue'
export {
  default as NvSheetFooter,
  /** @deprecated Use `NvSheetFooter` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as SheetProFooter,
} from './SheetProFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Nv names.
export {
  DialogClose as NvSheetClose,
  DialogRoot as NvSheet,
  DialogTrigger as NvSheetTrigger,
  /** @deprecated Use `NvSheetClose` (ADR 0020 NvUI); alias removed after codemod #789. */
  DialogClose as SheetProClose,
  /** @deprecated Use `NvSheet` (ADR 0020 NvUI); alias removed after codemod #789. */
  DialogRoot as SheetPro,
  /** @deprecated Use `NvSheetTrigger` (ADR 0020 NvUI); alias removed after codemod #789. */
  DialogTrigger as SheetProTrigger,
} from 'reka-ui'
