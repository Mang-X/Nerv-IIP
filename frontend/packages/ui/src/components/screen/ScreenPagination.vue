<script setup lang="ts">
import { ChevronLeft, ChevronRight } from 'lucide-vue-next'
import { computed } from 'vue'

/**
 * Screen — big-board pagination. Total + current range readout, prev/next, and a
 * windowed page list with ellipses (1 … p-1 p p+1 … N). The active page fills
 * cyan; controls press with a no-shift scale. `v-model:page` drives the current
 * page; `total` + `pageSize` derive the count. Built on the independent `--sb-*`
 * tokens.
 */
const page = defineModel<number>('page', { default: 1 })

const props = withDefaults(
  defineProps<{
    /** Total row count. */
    total: number
    /** Rows per page. */
    pageSize?: number
  }>(),
  { pageSize: 10 },
)

const pageCount = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))

/** Windowed page list with ellipses. */
const pages = computed<(number | '…')[]>(() => {
  const n = pageCount.value
  const c = page.value
  const out: (number | '…')[] = []
  const range = (a: number, b: number) => {
    for (let i = a; i <= b; i++) out.push(i)
  }
  if (n <= 7) {
    range(1, n)
  } else if (c <= 4) {
    range(1, 5)
    out.push('…', n)
  } else if (c >= n - 3) {
    out.push(1, '…')
    range(n - 4, n)
  } else {
    out.push(1, '…')
    range(c - 1, c + 1)
    out.push('…', n)
  }
  return out
})

const rangeText = computed(() => {
  if (props.total === 0) return '0'
  const from = (page.value - 1) * props.pageSize + 1
  const to = Math.min(props.total, page.value * props.pageSize)
  return `${from.toLocaleString('en-US')}–${to.toLocaleString('en-US')}`
})

function go(p: number) {
  const x = Math.max(1, Math.min(pageCount.value, p))
  if (x !== page.value) page.value = x
}
</script>

<template>
  <nav class="sb-pg" aria-label="分页">
    <span class="sb-pg-total">共 {{ total.toLocaleString('en-US') }} 条 · {{ rangeText }}</span>
    <div class="sb-pg-ctrl">
      <button
        type="button"
        class="sb-pg-btn"
        :disabled="page <= 1"
        aria-label="上一页"
        @click="go(page - 1)"
      >
        <ChevronLeft :size="16" />
      </button>
      <button
        v-for="(p, i) in pages"
        :key="i"
        type="button"
        class="sb-pg-num"
        :class="{ on: p === page, gap: p === '…' }"
        :disabled="p === '…'"
        :aria-current="p === page ? 'page' : undefined"
        @click="typeof p === 'number' && go(p)"
      >
        {{ p }}
      </button>
      <button
        type="button"
        class="sb-pg-btn"
        :disabled="page >= pageCount"
        aria-label="下一页"
        @click="go(page + 1)"
      >
        <ChevronRight :size="16" />
      </button>
    </div>
  </nav>
</template>

<style scoped>
.sb-pg {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  font-variant-numeric: tabular-nums;
}
.sb-pg-total {
  font-size: 13px;
  color: var(--sb-muted);
}
.sb-pg-ctrl {
  display: flex;
  align-items: center;
  gap: 4px;
}
.sb-pg-btn,
.sb-pg-num {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 32px;
  height: 32px;
  padding: 0 6px;
  border: 1px solid var(--sb-line-2);
  border-radius: calc(var(--sb-radius) - 1px);
  background: rgba(255, 255, 255, 0.03);
  color: var(--sb-text-2);
  font-size: 13px;
  font-variant-numeric: tabular-nums;
  cursor: pointer;
  transition:
    background 0.18s var(--sb-ease),
    border-color 0.18s var(--sb-ease),
    color 0.18s var(--sb-ease),
    transform 0.18s var(--sb-ease);
}
.sb-pg-btn:hover:not(:disabled),
.sb-pg-num:hover:not(:disabled):not(.on):not(.gap) {
  border-color: var(--sb-cyan-dim);
  color: var(--sb-text);
  background: rgba(0, 229, 255, 0.06);
}
/* press — pure scale, no shift */
.sb-pg-btn:active:not(:disabled),
.sb-pg-num:active:not(:disabled):not(.on):not(.gap) {
  transform: scale(0.94);
}
.sb-pg-num.on {
  background: var(--sb-accent-fill);
  border-color: var(--sb-accent-edge);
  color: #04203a;
  font-weight: 600;
}
.sb-pg-num.gap {
  border-color: transparent;
  background: transparent;
  color: var(--sb-faint);
  cursor: default;
}
.sb-pg-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.sb-pg-btn:focus-visible,
.sb-pg-num:focus-visible {
  outline: none;
  box-shadow: 0 0 0 2px var(--sb-bg), 0 0 0 4px var(--sb-cyan-dim);
}

@media (prefers-reduced-motion: reduce) {
  .sb-pg-btn,
  .sb-pg-num {
    transition: none;
  }
}
</style>
