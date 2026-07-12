export { default as NvDialogContent } from './NvDialogContent.vue'
export { default as NvDialogTitle } from './NvDialogTitle.vue'
export { default as NvDialogDescription } from './NvDialogDescription.vue'
export { default as NvDialogHeader } from './NvDialogHeader.vue'
export { default as NvDialogFooter } from './NvDialogFooter.vue'
// Root / trigger / close carry no styling — re-export reka under Nv names.
export {
  DialogClose as NvDialogClose,
  DialogRoot as NvDialog,
  DialogTrigger as NvDialogTrigger,
} from 'reka-ui'
