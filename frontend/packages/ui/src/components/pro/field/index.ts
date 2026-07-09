import type { VariantProps } from 'class-variance-authority'
import { cva } from 'class-variance-authority'

/**
 * Pro — copy-rebuilt field layout primitives (does NOT touch原版 Field).
 * These are layout/semantic components: structurally equivalent to the base
 * `ui/field` set but namespaced under the Pro layer (data-slot `field-pro*`).
 */
export const nvFieldVariants = cva(
  'data-[invalid=true]:text-destructive gap-2 group/field flex w-full',
  {
    variants: {
      orientation: {
        vertical: 'flex-col *:w-full [&>.sr-only]:w-auto',
        horizontal:
          'flex-row items-center has-[>[data-slot=field-pro-content]]:items-start *:data-[slot=field-pro-label]:flex-auto has-[>[data-slot=field-pro-content]]:[&>[role=checkbox],[role=radio]]:mt-px',
        responsive:
          'flex-col *:w-full @md/field-pro-group:flex-row @md/field-pro-group:items-center @md/field-pro-group:*:w-auto @md/field-pro-group:has-[>[data-slot=field-pro-content]]:items-start @md/field-pro-group:*:data-[slot=field-pro-label]:flex-auto [&>.sr-only]:w-auto @md/field-pro-group:has-[>[data-slot=field-pro-content]]:[&>[role=checkbox],[role=radio]]:mt-px',
      },
    },
    defaultVariants: {
      orientation: 'vertical',
    },
  },
)

export type NvFieldVariants = VariantProps<typeof nvFieldVariants>

export { default as NvField } from './FieldPro.vue'
export { default as NvFieldContent } from './FieldProContent.vue'
export { default as NvFieldDescription } from './FieldProDescription.vue'
export { default as NvFieldError } from './FieldProError.vue'
export { default as NvFieldGroup } from './FieldProGroup.vue'
export { default as NvFieldLabel } from './FieldProLabel.vue'
export { default as NvFieldLegend } from './FieldProLegend.vue'
export { default as NvFieldSeparator } from './FieldProSeparator.vue'
export { default as NvFieldSet } from './FieldProSet.vue'
export { default as NvFieldTitle } from './FieldProTitle.vue'
