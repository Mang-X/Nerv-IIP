import { shallowRef } from 'vue'

export function useSchedulingSelection<TSelection>() {
  const selected = shallowRef<TSelection>()

  function select(value: TSelection) {
    selected.value = value
  }

  function clearSelection() {
    selected.value = undefined
  }

  return {
    selected,
    select,
    clearSelection,
  }
}
