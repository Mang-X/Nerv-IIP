import { useScreenData } from '@nerv-iip/ui'
import { watch } from 'vue'
import { accessError } from '@/data/real/readError'

// A permission failure invalidates even a previously successful sample.
export function useAuthorizedScreenData<T>(
  fetcher: () => Promise<T>,
  options: { intervalMs: number },
) {
  const source = useScreenData(fetcher, options)
  watch(
    source.error,
    (error) => {
      if (accessError(error)) {
        source.data.value = undefined
        source.lastUpdated.value = undefined
      }
    },
    { flush: 'sync' },
  )
  return source
}
