import { nextTick, useTemplateRef } from 'vue'

export function useFirstInvalidField() {
  const formElement = useTemplateRef<HTMLFormElement>('formElement')

  async function focusFirstInvalidField() {
    await nextTick()
    const field = formElement.value?.querySelector<HTMLElement>(
      '[aria-invalid="true"]:is(input, button, select, textarea, [tabindex]):not(:disabled):not([aria-disabled="true"])',
    )
    field?.focus({ preventScroll: true })
    field?.scrollIntoView?.({ block: 'nearest' })
  }

  return { formElement, focusFirstInvalidField }
}
