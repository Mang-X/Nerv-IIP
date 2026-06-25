export { default as SheetProContent } from './SheetProContent.vue'
export { default as SheetProTitle } from './SheetProTitle.vue'
export { default as SheetProDescription } from './SheetProDescription.vue'
export { default as SheetProHeader } from './SheetProHeader.vue'
export { default as SheetProFooter } from './SheetProFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Pro names.
export {
  DialogClose as SheetProClose,
  DialogRoot as SheetPro,
  DialogTrigger as SheetProTrigger,
} from 'reka-ui'
