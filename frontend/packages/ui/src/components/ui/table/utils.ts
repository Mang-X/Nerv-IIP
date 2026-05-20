import type { Ref } from 'vue'

export type Updater<T> = T | ((old: T) => T)

function isUpdaterFunction<T>(
  updaterOrValue: Updater<T>,
): updaterOrValue is (old: T) => T {
  return typeof updaterOrValue === 'function'
}

export function valueUpdater<T>(updaterOrValue: Updater<T>, ref: Ref<T>) {
  ref.value = isUpdaterFunction(updaterOrValue)
    ? updaterOrValue(ref.value)
    : updaterOrValue
}
