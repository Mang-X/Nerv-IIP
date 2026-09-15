<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref } from 'vue'
import { Search, X } from '@lucide/vue'
import { cn } from '../../lib/utils'

/**
 * Mobile SearchBar — rounded pill search (Vant / tdesign-mobile style). On focus
 * the Cancel action slides in (animated width) and the field smoothly shrinks;
 * a clear button fades in when there is text.
 */
withDefaults(
  defineProps<{
    placeholder?: string
    ariaLabel?: string
    cancelable?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { placeholder: '搜索', cancelable: false },
)
const emit = defineEmits<{ search: [value: string]; cancel: [] }>()
const model = defineModel<string>({ default: '' })
const focused = ref(false)

const expanded = computed(() => focused.value || !!model.value)

/**
 * 用户把关键词清空（点清除按钮、或退格删到空）是一次明确的「回到全量」检索意图，必须让
 * 只监听 `@search` 的消费方看得见：否则输入框空了、列表还按旧关键词过滤，空结果还会继续
 * 显示「没有匹配的 XXX」。这条错位不是观感问题——工人据此判断本组织没配这个码，转而走
 * 「不登记」分支，本该记录的数据就丢了。
 *
 * 走计算属性的写入面而不是 `watch(model)`：只有 `v-model` 的写入（= 用户敲键盘、或本组件
 * 的清除按钮）才经过这里，父组件自己把绑定值置空（如打开抽屉时重置）不会误触发重查。
 *
 * `cancel` 的语义不同：它是「退出搜索」，收起还是重查由消费方通过 `@cancel` 自己决定，
 * 所以那条路径**不**顺手 emit `search`。
 */
const keyword = computed<string>({
  get: () => model.value,
  set: (value) => {
    model.value = value
    if (value === '') emit('search', '')
  },
})

function clear() {
  keyword.value = ''
}
function cancel() {
  model.value = ''
  focused.value = false
  emit('cancel')
}
</script>

<template>
  <div data-slot="search-bar" :class="cn('flex items-center px-3 py-2', $props.class)">
    <div class="min-h-touch flex flex-1 items-center gap-2 rounded-full bg-muted px-3.5">
      <Search class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <input
        v-model="keyword"
        type="search"
        :placeholder="placeholder"
        :aria-label="ariaLabel"
        class="min-h-touch w-full min-w-0 bg-transparent text-[15px] outline-none placeholder:text-muted-foreground [&::-webkit-search-cancel-button]:hidden"
        @focus="focused = true"
        @keydown.enter="emit('search', model)"
      />
      <Transition name="nv-m-sb-clear">
        <button
          v-if="model"
          type="button"
          class="min-h-touch min-w-12 -my-1 -mr-3 grid shrink-0 place-items-center rounded-full text-card active:opacity-70"
          aria-label="清除"
          @click="clear"
        >
          <span class="grid size-5 place-items-center rounded-full bg-muted-foreground/30">
            <X class="size-3.5" aria-hidden="true" />
          </span>
        </button>
      </Transition>
    </div>
    <div v-if="cancelable" class="nv-m-sb-cancel" :class="expanded && 'is-open'">
      <button
        type="button"
        class="min-h-touch min-w-12 text-[15px] whitespace-nowrap text-brand active:opacity-60"
        @click="cancel"
      >
        取消
      </button>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  /* Cancel slides in by animating its track width; the flex field shrinks with it. */
  .nv-m-sb-cancel {
    max-width: 0;
    opacity: 0;
    overflow: hidden;
    transition:
      max-width 0.28s var(--nv-ease-out-expo),
      opacity 0.2s ease;
  }
  .nv-m-sb-cancel.is-open {
    max-width: 4rem;
    opacity: 1;
  }
  .nv-m-sb-cancel > button {
    padding-left: 0.5rem;
  }

  .nv-m-sb-clear-enter-active,
  .nv-m-sb-clear-leave-active {
    transition:
      opacity 0.15s ease,
      transform 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .nv-m-sb-clear-enter-from,
  .nv-m-sb-clear-leave-to {
    opacity: 0;
    transform: scale(0.6);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-m-sb-cancel {
      transition: opacity 0.15s linear;
    }
    .nv-m-sb-clear-enter-active,
    .nv-m-sb-clear-leave-active {
      transition: opacity 0.12s linear;
    }
  }
}
</style>
