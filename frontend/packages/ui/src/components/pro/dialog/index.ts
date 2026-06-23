export { default as DialogProContent } from './DialogProContent.vue'
export { default as DialogProTitle } from './DialogProTitle.vue'
export { default as DialogProDescription } from './DialogProDescription.vue'
export { default as DialogProHeader } from './DialogProHeader.vue'
export { default as DialogProFooter } from './DialogProFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Pro names.
export {
  DialogClose as DialogProClose,
  DialogRoot as DialogPro,
  DialogTrigger as DialogProTrigger,
} from 'reka-ui'
