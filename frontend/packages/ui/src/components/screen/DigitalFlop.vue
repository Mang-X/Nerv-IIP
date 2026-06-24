<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — digital flip-counter. Each digit sits in its own dark cell with a top
 * highlight and a cyan glowing glyph; thousands are grouped with thin comma
 * cells. Pass a number (grouped automatically) or a pre-formatted string. Built
 * on the independent `--sb-*` tokens.
 */
const props = withDefaults(
  defineProps<{
    /** A number (auto thousands-grouped) or a ready string like "1,284". */
    value: number | string
    /** Small unit after the counter, e.g. kWh. */
    suffix?: string
  }>(),
  {},
)

/** Group integers; pass strings through verbatim so callers can pre-format. */
const text = computed(() =>
  typeof props.value === 'number' ? props.value.toLocaleString('en-US') : String(props.value),
)

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
.sb-df-cell {
  position: relative;
  min-width: 30px;
  padding: 6px 0 7px;
  border-radius: 5px;
  background: linear-gradient(180deg, #0c1626, #07101d);
  border: 1px solid var(--sb-line);
  text-align: center;
  font-size: 30px;
  font-weight: 700;
  line-height: 1;
  color: var(--sb-cyan);
  text-shadow: 0 0 14px rgba(0, 229, 255, 0.45);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.06),
    inset 0 -8px 14px -10px rgba(0, 0, 0, 0.8);
}
/* top glass highlight */
.sb-df-cell::before {
  content: '';
  position: absolute;
  inset: 1px 1px auto 1px;
  height: 42%;
  border-radius: 4px 4px 0 0;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.07), transparent);
  pointer-events: none;
}
/* comma / dot / space — a thin transparent cell, no box */
.sb-df-cell.sep {
  min-width: 0;
  padding-left: 1px;
  padding-right: 1px;
  background: none;
  border: none;
  box-shadow: none;
  color: var(--sb-cyan-dim);
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
