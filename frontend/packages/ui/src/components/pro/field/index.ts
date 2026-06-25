import type { VariantProps } from 'class-variance-authority'
import { cva } from 'class-variance-authority'

/**
 * Pro — copy-rebuilt field layout primitives (does NOT touch原版 Field).
 * These are layout/semantic components: structurally equivalent to the base
 * `ui/field` set but namespaced under the Pro layer (data-slot `field-pro*`).
 */
export const fieldProVariants = cva(
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

export type FieldProVariants = VariantProps<typeof fieldProVariants>

export { default as FieldPro } from './FieldPro.vue'
export { default as FieldProContent } from './FieldProContent.vue'
export { default as FieldProDescription } from './FieldProDescription.vue'
export { default as FieldProError } from './FieldProError.vue'
export { default as FieldProGroup } from './FieldProGroup.vue'
export { default as FieldProLabel } from './FieldProLabel.vue'
export { default as FieldProLegend } from './FieldProLegend.vue'
export { default as FieldProSeparator } from './FieldProSeparator.vue'
export { default as FieldProSet } from './FieldProSet.vue'
export { default as FieldProTitle } from './FieldProTitle.vue'
