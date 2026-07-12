import type { VariantProps } from 'class-variance-authority'
import { cva } from 'class-variance-authority'

/**
 * Pro — copy-rebuilt field layout primitives (does NOT touch原版 Field).
 * These are layout/semantic components: structurally equivalent to the base
 * `ui/field` set but namespaced under the Pro layer (data-slot `nv-field*`).
 */
export const nvFieldVariants = cva(
  'data-[invalid=true]:text-destructive gap-2 group/field flex w-full',
  {
    variants: {
      orientation: {
        vertical: 'flex-col *:w-full [&>.sr-only]:w-auto',
        horizontal:
          'flex-row items-center has-[>[data-slot=nv-field-content]]:items-start *:data-[slot=nv-field-label]:flex-auto has-[>[data-slot=nv-field-content]]:[&>[role=checkbox],[role=radio]]:mt-px',
        responsive:
          'flex-col *:w-full @md/nv-field-group:flex-row @md/nv-field-group:items-center @md/nv-field-group:*:w-auto @md/nv-field-group:has-[>[data-slot=nv-field-content]]:items-start @md/nv-field-group:*:data-[slot=nv-field-label]:flex-auto [&>.sr-only]:w-auto @md/nv-field-group:has-[>[data-slot=nv-field-content]]:[&>[role=checkbox],[role=radio]]:mt-px',
      },
    },
    defaultVariants: {
      orientation: 'vertical',
    },
  },
)

export type NvFieldVariants = VariantProps<typeof nvFieldVariants>

export { default as NvField } from './NvField.vue'
export { default as NvFieldContent } from './NvFieldContent.vue'
export { default as NvFieldDescription } from './NvFieldDescription.vue'
export { default as NvFieldError } from './NvFieldError.vue'
export { default as NvFieldGroup } from './NvFieldGroup.vue'
export { default as NvFieldLabel } from './NvFieldLabel.vue'
export { default as NvFieldLegend } from './NvFieldLegend.vue'
export { default as NvFieldSeparator } from './NvFieldSeparator.vue'
export { default as NvFieldSet } from './NvFieldSet.vue'
export { default as NvFieldTitle } from './NvFieldTitle.vue'
