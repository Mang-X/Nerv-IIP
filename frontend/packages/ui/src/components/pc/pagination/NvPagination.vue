<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref, watch } from 'vue'
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  ChevronsLeftIcon,
  ChevronsRightIcon,
} from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import { NvSelect, NvSelectContent, NvSelectItem, NvSelectTrigger, NvSelectValue } from '../select'

/**
 * Pro — premium pagination. Clickable numbered pages with ellipsis truncation,
 * first/last + prev/next chevrons, a page-size select, a live result summary,
 * and an optional jump-to-page field. Brand-filled current page, calibrated
 * hover/active feedback, reduced-motion safe.
 */
const props = withDefaults(
  defineProps<{
    page: number
    /** Accepts a string too, so it drops in over `usePagedList`'s string pageSize. */
    pageSize: number | string
    totalItems: number
    pageSizeOptions?: number[]
    /** Sibling page count on each side of the current page. */
    siblingCount?: number
    /** Show the jump-to-page field. */
    showJump?: boolean
    /** Show first/last edge buttons. */
    showEdges?: boolean
    class?: HTMLAttributes['class']
  }>(),
  {
    pageSizeOptions: () => [10, 20, 50, 100],
    siblingCount: 1,
    showJump: false,
    showEdges: true,
  },
)

const emit = defineEmits<{
  'update:page': [value: number]
  'update:pageSize': [value: number]
}>()

// Normalise to a number — pageSize may arrive as a string (e.g. from usePagedList).
const size = computed(() => Number(props.pageSize) || 10)

// Always keep the active size selectable. reka's SelectValue shows nothing when
// the bound value has no matching SelectItem, so a pageSize outside the provided
// options (e.g. 8 with the default [10,20,50,100]) would leave the trigger blank.
const sizeOptions = computed(() =>
  props.pageSizeOptions.includes(size.value)
    ? props.pageSizeOptions
    : [...props.pageSizeOptions, size.value].sort((a, b) => a - b),
)
const totalPages = computed(() => Math.max(1, Math.ceil(props.totalItems / size.value)))
const currentPage = computed(() => Math.min(Math.max(1, props.page), totalPages.value))

// Correct an out-of-range parent page (e.g. data shrank under the active window).
watch([totalPages, () => props.page], ([pages, page]) => {
  if (page > pages) emit('update:page', pages)
})

const summary = computed(() => {
  if (props.totalItems <= 0) return '0 条'
  const start = (currentPage.value - 1) * size.value + 1
  const end = Math.min(currentPage.value * size.value, props.totalItems)
  return `${start}–${end} / ${props.totalItems} 条`
})

// Page-number model: first, last, current ± siblings, with '…' for gaps.
type Slot = number | 'ellipsis-l' | 'ellipsis-r'
const pageSlots = computed<Slot[]>(() => {
  const total = totalPages.value
  const cur = currentPage.value
  const sib = props.siblingCount
  if (total <= 5 + sib * 2) return Array.from({ length: total }, (_, i) => i + 1)

  const left = Math.max(2, cur - sib)
  const right = Math.min(total - 1, cur + sib)
  const slots: Slot[] = [1]
  if (left > 2) slots.push('ellipsis-l')
  for (let p = left; p <= right; p++) slots.push(p)
  if (right < total - 1) slots.push('ellipsis-r')
  slots.push(total)
  return slots
})

function go(page: number) {
  const next = Math.min(Math.max(1, page), totalPages.value)
  if (next !== currentPage.value) emit('update:page', next)
}

function onPageSize(value: unknown) {
  const n = Number(value)
  if (!Number.isFinite(n)) return
  emit('update:pageSize', n)
  emit('update:page', 1)
}

// Jump-to-page: commit on Enter / blur, clamp silently, then clear.
const jump = ref('')
function commitJump() {
  const n = Number(jump.value)
  if (Number.isFinite(n) && n >= 1) go(Math.round(n))
  jump.value = ''
}
</script>

<template>
  <div
    :class="cn('flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between', props.class)"
  >
    <div class="flex items-center gap-3">
      <p class="text-sm tabular-nums text-muted-foreground" aria-live="polite">
        显示 {{ summary }}
      </p>
      <div class="hidden items-center gap-2 sm:flex">
        <span class="text-sm text-muted-foreground">每页</span>
        <NvSelect :model-value="String(pageSize)" @update:model-value="onPageSize">
          <NvSelectTrigger class="h-8 w-[4.5rem]" aria-label="每页条数">
            <NvSelectValue />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="opt in sizeOptions" :key="opt" :value="String(opt)">
              {{ opt }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </div>
    </div>

    <nav class="flex items-center gap-1" aria-label="分页">
      <button
        v-if="showEdges"
        type="button"
        class="nv-pg-btn rounded-[7px] enabled:hover:bg-muted enabled:hover:text-foreground"
        :disabled="currentPage <= 1"
        aria-label="第一页"
        @click="go(1)"
      >
        <ChevronsLeftIcon class="size-4" aria-hidden="true" />
      </button>
      <button
        type="button"
        class="nv-pg-btn rounded-[7px] enabled:hover:bg-muted enabled:hover:text-foreground"
        :disabled="currentPage <= 1"
        aria-label="上一页"
        @click="go(currentPage - 1)"
      >
        <ChevronLeftIcon class="size-4" aria-hidden="true" />
      </button>

      <!-- Keys are content-stable (page number or fixed ellipsis side) so Vue reuses /
           moves nodes instead of remounting them — that remount churn is what flickered. -->
      <template v-for="slot in pageSlots" :key="slot">
        <button
          v-if="typeof slot === 'string'"
          type="button"
          class="nv-pg-gap rounded-[7px] hover:bg-muted hover:text-foreground"
          :aria-label="slot === 'ellipsis-l' ? '向前 5 页' : '向后 5 页'"
          @click="go(currentPage + (slot === 'ellipsis-l' ? -5 : 5))"
        >
          <span class="nv-pg-gap-dots" aria-hidden="true">…</span>
          <ChevronsLeftIcon
            v-if="slot === 'ellipsis-l'"
            class="nv-pg-gap-jump size-3.5"
            aria-hidden="true"
          />
          <ChevronsRightIcon v-else class="nv-pg-gap-jump size-3.5" aria-hidden="true" />
        </button>
        <button
          v-else
          type="button"
          class="nv-pg-num rounded-[7px] not-data-[active]:hover:bg-muted data-[active]:bg-[var(--nv-brand)] data-[active]:text-[var(--nv-brand-foreground)] data-[active]:font-semibold"
          :data-active="slot === currentPage || undefined"
          :aria-current="slot === currentPage ? 'page' : undefined"
          :aria-label="`第 ${slot} 页`"
          @click="go(slot)"
        >
          {{ slot }}
        </button>
      </template>

      <button
        type="button"
        class="nv-pg-btn rounded-[7px] enabled:hover:bg-muted enabled:hover:text-foreground"
        :disabled="currentPage >= totalPages"
        aria-label="下一页"
        @click="go(currentPage + 1)"
      >
        <ChevronRightIcon class="size-4" aria-hidden="true" />
      </button>
      <button
        v-if="showEdges"
        type="button"
        class="nv-pg-btn rounded-[7px] enabled:hover:bg-muted enabled:hover:text-foreground"
        :disabled="currentPage >= totalPages"
        aria-label="最后一页"
        @click="go(totalPages)"
      >
        <ChevronsRightIcon class="size-4" aria-hidden="true" />
      </button>

      <label
        v-if="showJump"
        class="ml-1 hidden items-center gap-1.5 text-sm text-muted-foreground lg:flex"
      >
        跳至
        <input
          v-model="jump"
          inputmode="numeric"
          class="nv-pg-jump rounded-[7px] border border-border bg-card"
          aria-label="跳至页码"
          @keydown.enter.prevent="commitJump"
          @blur="commitJump"
        />
        页
      </label>
    </nav>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-pg-btn,
  .nv-pg-num {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 2rem;
    min-width: 2rem;
    padding-inline: 0.4rem;
    border-radius: 7px;
    font-size: 0.8125rem;
    font-variant-numeric: tabular-nums;
    color: var(--foreground);
    outline: none;
    /* Page numbers: no background/color transition. When the page changes the
     active brand fill must move instantly between numbers — a cross-fade left
     two numbers highlighted for ~150ms, which read as a flicker on click. */
    transition:
      box-shadow 0.15s var(--nv-ease-out-quart, ease-out),
      transform 0.12s var(--nv-ease-out-quart, ease-out);
  }
  /* Prev / next / edge buttons never hand off an active state, so a soft hover
   fade is safe and adds polish. */
  .nv-pg-btn {
    color: var(--muted-foreground);
    transition:
      background-color 0.15s var(--nv-ease-out-quart, ease-out),
      color 0.15s var(--nv-ease-out-quart, ease-out),
      box-shadow 0.15s var(--nv-ease-out-quart, ease-out),
      transform 0.12s var(--nv-ease-out-quart, ease-out);
  }
  .nv-pg-btn:hover:not(:disabled),
  .nv-pg-num:hover:not([data-active]) {
    background-color: var(--muted);
    color: var(--foreground);
  }
  .nv-pg-btn:active:not(:disabled),
  .nv-pg-num:active:not([data-active]) {
    transform: scale(0.94);
  }
  .nv-pg-btn:disabled {
    opacity: 0.4;
    pointer-events: none;
  }
  .nv-pg-btn:focus-visible,
  .nv-pg-num:focus-visible,
  .nv-pg-jump:focus-visible {
    box-shadow: 0 0 0 3px color-mix(in oklch, var(--ring) 45%, transparent);
  }
  .nv-pg-num[data-active] {
    background-color: var(--nv-brand);
    color: var(--nv-brand-foreground);
    font-weight: 600;
    box-shadow:
      inset 0 1px 0 0 color-mix(in oklch, white 16%, transparent),
      0 1px 2px 0 color-mix(in oklch, black 24%, transparent);
  }
  /* Ellipsis doubles as a jump-by-5 control: '…' by default, double-chevron on hover. */
  .nv-pg-gap {
    position: relative;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    height: 2rem;
    min-width: 2rem;
    border-radius: 7px;
    color: var(--muted-foreground);
    user-select: none;
    outline: none;
    transition:
      background-color 0.15s var(--nv-ease-out-quart, ease-out),
      color 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .nv-pg-gap:hover {
    background-color: var(--muted);
    color: var(--foreground);
  }
  .nv-pg-gap:focus-visible {
    box-shadow: 0 0 0 3px color-mix(in oklch, var(--ring) 45%, transparent);
  }
  .nv-pg-gap-dots,
  .nv-pg-gap-jump {
    transition: opacity 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .nv-pg-gap-jump {
    position: absolute;
    opacity: 0;
  }
  .nv-pg-gap:hover .nv-pg-gap-dots {
    opacity: 0;
  }
  .nv-pg-gap:hover .nv-pg-gap-jump {
    opacity: 1;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-pg-gap,
    .nv-pg-gap-dots,
    .nv-pg-gap-jump {
      transition: none;
    }
  }
  .nv-pg-jump {
    width: 3rem;
    height: 2rem;
    border-radius: 7px;
    border: 1px solid var(--border);
    background-color: var(--card);
    text-align: center;
    font-size: 0.8125rem;
    font-variant-numeric: tabular-nums;
    outline: none;
    transition:
      border-color 0.15s var(--nv-ease-out-quart, ease-out),
      box-shadow 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .nv-pg-jump:focus-visible {
    border-color: var(--nv-brand);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-pg-btn,
    .nv-pg-num {
      transition:
        background-color 0.1s linear,
        color 0.1s linear;
    }
    .nv-pg-btn:active:not(:disabled),
    .nv-pg-num:active:not([data-active]) {
      transform: none;
    }
  }
}
</style>
