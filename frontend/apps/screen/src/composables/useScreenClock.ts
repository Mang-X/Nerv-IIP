import { createSharedComposable, useNow } from '@vueuse/core'

export const useScreenClock = createSharedComposable(() => useNow({ interval: 1000 }))
