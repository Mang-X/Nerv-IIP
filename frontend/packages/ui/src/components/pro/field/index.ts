import type { VariantProps } from 'class-variance-authority'
import { cva } from 'class-variance-authority'

/**
 * Pro — copy-rebuilt field layout primitives (does NOT touch原版 Field).
 * These are layout/semantic components: structurally equivalent to the base
 * `ui/field` set but namespaced under the Pro layer (data-slot `field-pro*`).
 *
 * @deprecated Use `nvFieldVariants` (ADR 0020 NvUI); alias removed after codemod #789.
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

/** Canonical NvUI alias of {@link fieldProVariants} (ADR 0020). */
export const nvFieldVariants = fieldProVariants

/** @deprecated Use `NvFieldVariants` (ADR 0020 NvUI); alias removed after codemod #789. */
export type FieldProVariants = VariantProps<typeof fieldProVariants>
export type NvFieldVariants = FieldProVariants

export {
  default as NvField,
  /** @deprecated Use `NvField` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldPro,
} from './FieldPro.vue'
export {
  default as NvFieldContent,
  /** @deprecated Use `NvFieldContent` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldProContent,
} from './FieldProContent.vue'
export {
  default as NvFieldDescription,
  /** @deprecated Use `NvFieldDescription` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldProDescription,
} from './FieldProDescription.vue'
export {
  default as NvFieldError,
  /** @deprecated Use `NvFieldError` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldProError,
} from './FieldProError.vue'
export {
  default as NvFieldGroup,
  /** @deprecated Use `NvFieldGroup` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldProGroup,
} from './FieldProGroup.vue'
export {
  default as NvFieldLabel,
  /** @deprecated Use `NvFieldLabel` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldProLabel,
} from './FieldProLabel.vue'
export {
  default as NvFieldLegend,
  /** @deprecated Use `NvFieldLegend` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldProLegend,
} from './FieldProLegend.vue'
export {
  default as NvFieldSeparator,
  /** @deprecated Use `NvFieldSeparator` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldProSeparator,
} from './FieldProSeparator.vue'
export {
  default as NvFieldSet,
  /** @deprecated Use `NvFieldSet` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldProSet,
} from './FieldProSet.vue'
export {
  default as NvFieldTitle,
  /** @deprecated Use `NvFieldTitle` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as FieldProTitle,
} from './FieldProTitle.vue'
