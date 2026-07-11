export { default as NvDialogContent } from './DialogProContent.vue'
export { default as NvDialogTitle } from './DialogProTitle.vue'
export { default as NvDialogDescription } from './DialogProDescription.vue'
export { default as NvDialogHeader } from './DialogProHeader.vue'
export { default as NvDialogFooter } from './DialogProFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Nv names.
export {
  DialogClose as NvDialogClose,
  DialogRoot as NvDialog,
  DialogTrigger as NvDialogTrigger,
} from 'reka-ui'
