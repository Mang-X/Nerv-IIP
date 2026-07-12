export { default as NvSheetContent } from './NvSheetContent.vue'
export { default as NvSheetTitle } from './NvSheetTitle.vue'
export { default as NvSheetDescription } from './NvSheetDescription.vue'
export { default as NvSheetHeader } from './NvSheetHeader.vue'
export { default as NvSheetFooter } from './NvSheetFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Nv names.
export {
  DialogClose as NvSheetClose,
  DialogRoot as NvSheet,
  DialogTrigger as NvSheetTrigger,
} from 'reka-ui'
