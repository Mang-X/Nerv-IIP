import { onMounted, onUnmounted, toValue, type MaybeRefOrGetter } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'

export function usePendingWriteLeaveGuard(locked: MaybeRefOrGetter<boolean>) {
  onBeforeRouteLeave(() => !toValue(locked))

  function preventRefresh(event: BeforeUnloadEvent) {
    if (!toValue(locked)) return
    event.preventDefault()
    event.returnValue = true
  }

  onMounted(() => window.addEventListener('beforeunload', preventRefresh))
  onUnmounted(() => window.removeEventListener('beforeunload', preventRefresh))
}
