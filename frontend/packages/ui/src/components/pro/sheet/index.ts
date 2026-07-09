export { default as NvSheetContent } from './SheetProContent.vue'
export { default as NvSheetTitle } from './SheetProTitle.vue'
export { default as NvSheetDescription } from './SheetProDescription.vue'
export { default as NvSheetHeader } from './SheetProHeader.vue'
export { default as NvSheetFooter } from './SheetProFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Nv names.
export {
  DialogClose as NvSheetClose,
  DialogRoot as NvSheet,
  DialogTrigger as NvSheetTrigger,
} from 'reka-ui'
