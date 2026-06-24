<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import { computed, onBeforeUnmount, ref, watch } from 'vue'

/**
 * Screen — digital flip-counter. Each digit sits in its own dark cell with a top
 * highlight and a cyan glowing glyph; thousands are grouped with thin comma cells.
 * A numeric value rolls (tweens) to its new figure on change — the "flip" — over
 * 0.6s on the emphasized curve; a pre-formatted string shows verbatim. Under
 * reduced-motion the value snaps. Built on the independent `--sb-*` tokens.
 */
const props = withDefaults(
  defineProps<{
    /** A number (auto thousands-grouped, rolls on change) or a ready string like "1,284". */
    value: number | string
    /** Small unit after the counter, e.g. kWh. */
    suffix?: string
  }>(),
  {},
)

const reduce = useMediaQuery('(prefers-reduced-motion: reduce)')

// Tweened display figure for numeric values; strings pass straight through.
const display = ref(typeof props.value === 'number' ? props.value : 0)
let raf = 0

watch(
  () => props.value,
  (to) => {
    if (typeof to !== 'number') return
    cancelAnimationFrame(raf)
    if (reduce.value) {
      display.value = to
      return
    }
    const from = display.value
    const dur = 600
    let start = 0
    const step = (ts: number) => {
      if (!start) start = ts
      const t = Math.min(1, (ts - start) / dur)
      // ease-out-expo — matches --sb-ease-emphasized
      const e = t === 1 ? 1 : 1 - 2 ** (-10 * t)
      display.value = from + (to - from) * e
      if (t < 1) raf = requestAnimationFrame(step)
    }
    raf = requestAnimationFrame(step)
  },
)
onBeforeUnmount(() => cancelAnimationFrame(raf))

/** Group numbers (tweened), preserving the caller's decimal places; strings pass
 *  through verbatim so callers can pre-format. */
const text = computed(() => {
  if (typeof props.value !== 'number') return String(props.value)
  const dp = (String(props.value).split('.')[1] ?? '').length
  return display.value.toLocaleString('en-US', {
    minimumFractionDigits: dp,
    maximumFractionDigits: dp,
  })
})

/** One cell per character; commas/dots/spaces render as thin separators. */
const cells = computed(() =>
  [...text.value].map((ch) => {
    const digit = ch >= '0' && ch <= '9'
    return { ch, sep: !digit }
  }),
)
</script>

<template>
  <div class="sb-df" role="img" :aria-label="`${text}${suffix ? ` ${suffix}` : ''}`">
    <span
      v-for="(c, i) in cells"
      :key="i"
      class="sb-df-cell"
      :class="{ sep: c.sep }"
      aria-hidden="true"
    >{{ c.ch }}</span>
    <span v-if="suffix" class="sb-df-suffix">{{ suffix }}</span>
  </div>
</template>

<style scoped>
.sb-df {
  display: inline-flex;
  align-items: flex-end;
  gap: 4px;
  font-variant-numeric: tabular-nums;
}
/* modern flat figure — no skeuomorphic cell box, just a clean white glyph */
.sb-df-cell {
  position: relative;
  min-width: 0;
  text-align: center;
  font-size: 40px;
  font-weight: 700;
  line-height: 1;
  letter-spacing: 0.01em;
  color: #fff;
  text-shadow: var(--sb-value-glow);
}
/* comma / dot / space — a thin transparent cell, no box */
.sb-df-cell.sep {
  min-width: 0;
  padding-left: 1px;
  padding-right: 1px;
  background: none;
  border: none;
  box-shadow: none;
  color: var(--sb-muted);
  text-shadow: none;
}
.sb-df-cell.sep::before {
  display: none;
}
.sb-df-suffix {
  margin-left: 6px;
  font-size: 14px;
  font-weight: 500;
  color: var(--sb-muted);
}
</style>
