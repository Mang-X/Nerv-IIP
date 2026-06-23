<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref } from 'vue'
import { Search, X } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Mobile SearchBar — rounded pill search (Vant / tdesign-mobile style). On focus
 * the Cancel action slides in (animated width) and the field smoothly shrinks;
 * a clear button fades in when there is text.
 */
withDefaults(
  defineProps<{
    placeholder?: string
    cancelable?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { placeholder: '搜索', cancelable: false },
)
const emit = defineEmits<{ search: [value: string]; cancel: [] }>()
const model = defineModel<string>({ default: '' })
const focused = ref(false)

const expanded = computed(() => focused.value || !!model.value)

function clear() {
  model.value = ''
}
function cancel() {
  model.value = ''
  focused.value = false
  emit('cancel')
}
</script>

<template>
  <div data-slot="search-bar" :class="cn('flex items-center px-3 py-2', $props.class)">
    <div class="flex h-9 flex-1 items-center gap-2 rounded-full bg-muted px-3.5">
      <Search class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <input
        v-model="model"
        type="search"
        :placeholder="placeholder"
        class="h-full w-full min-w-0 bg-transparent text-[15px] outline-none placeholder:text-muted-foreground [&::-webkit-search-cancel-button]:hidden"
        @focus="focused = true"
        @keydown.enter="emit('search', model)"
      />
      <Transition name="ds-sb-clear">
        <button
          v-if="model"
          type="button"
          class="grid size-5 shrink-0 place-items-center rounded-full bg-muted-foreground/30 text-card active:opacity-70"
          aria-label="清除"
          @click="clear"
        >
          <X class="size-3.5" aria-hidden="true" />
        </button>
      </Transition>
    </div>
    <div v-if="cancelable" class="ds-sb-cancel" :class="expanded && 'is-open'">
      <button
        type="button"
        class="px-1 text-[15px] whitespace-nowrap text-brand active:opacity-60"
        @click="cancel"
      >
        取消
      </button>
    </div>
  </div>
</template>

<style scoped>
/* Cancel slides in by animating its track width; the flex field shrinks with it. */
.ds-sb-cancel {
  max-width: 0;
  opacity: 0;
  overflow: hidden;
  transition:
    max-width 0.28s var(--ease-out-expo, cubic-bezier(0.16, 1, 0.3, 1)),
    opacity 0.2s ease;
}
.ds-sb-cancel.is-open {
  max-width: 4rem;
  opacity: 1;
}
.ds-sb-cancel > button {
  padding-left: 0.5rem;
}

.ds-sb-clear-enter-active,
.ds-sb-clear-leave-active {
  transition:
    opacity 0.15s ease,
    transform 0.15s var(--ease-out-quart, ease-out);
}
.ds-sb-clear-enter-from,
.ds-sb-clear-leave-to {
  opacity: 0;
  transform: scale(0.6);
}

@media (prefers-reduced-motion: reduce) {
  .ds-sb-cancel {
    transition: opacity 0.15s linear;
  }
  .ds-sb-clear-enter-active,
  .ds-sb-clear-leave-active {
    transition: opacity 0.12s linear;
  }
}
</style>
