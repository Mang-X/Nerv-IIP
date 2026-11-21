import { createSharedComposable, useIntervalFn, useNow } from '@vueuse/core'

export const useScreenClock = createSharedComposable(() =>
  useNow({ scheduler: (tick) => useIntervalFn(tick, 1000) }),
)
