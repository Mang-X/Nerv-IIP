<script setup lang="ts">
/**
 * Screen — big-board data table. A faintly glowing header row over hairline body
 * rows (no vertical rules); rows light on hover. Columns set their own
 * alignment, and any cell can be taken over by a `#cell-<key>` slot for status
 * dots, mono codes, etc. Pure data — `columns` + `rows` drive it. Built on the
 * independent `--sb-*` tokens.
 */
interface Column {
  key: string
  label: string
  align?: 'left' | 'center' | 'right'
}

withDefaults(
  defineProps<{
    columns?: Column[]
    rows?: Record<string, unknown>[]
    /** Which column key uniquely identifies a row (for the v-for key). */
    rowKey?: string
  }>(),
  {
    columns: () => [
      { key: 'wo', label: '工单号' },
      { key: 'line', label: '产线' },
      { key: 'product', label: '产品' },
      { key: 'plan', label: '计划', align: 'right' },
      { key: 'actual', label: '实际', align: 'right' },
      { key: 'rate', label: '达成率', align: 'right' },
    ],
    rowKey: 'wo',
    rows: () => [
      { wo: 'WO-2406-0421', line: '焊接线 A', product: '车架总成', plan: '1,240', actual: '1,156', rate: '93.2%' },
      { wo: 'WO-2406-0418', line: '装配线 B', product: '门板组件', plan: '980', actual: '742', rate: '75.7%' },
      { wo: 'WO-2406-0415', line: 'CNC 线 C', product: '齿轮箱体', plan: '760', actual: '312', rate: '41.1%' },
      { wo: 'WO-2406-0409', line: '涂装线 D', product: '外壳面板', plan: '1,080', actual: '1,024', rate: '94.8%' },
    ],
  },
)
</script>

<template>
  <div class="sb-tbl-wrap">
    <table class="sb-tbl">
      <thead>
        <tr>
          <th
            v-for="c in columns"
            :key="c.key"
            :style="{ textAlign: c.align ?? 'left' }"
          >
            {{ c.label }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(row, i) in rows" :key="String(rowKey ? row[rowKey] : i)">
          <td
            v-for="c in columns"
            :key="c.key"
            :style="{ textAlign: c.align ?? 'left' }"
          >
            <slot :name="`cell-${c.key}`" :row="row" :value="row[c.key]">
              {{ row[c.key] }}
            </slot>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.sb-tbl-wrap {
  width: 100%;
  overflow-x: auto;
}
.sb-tbl {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}
.sb-tbl th {
  position: sticky;
  top: 0;
  padding: 11px 10px;
  font-weight: 500;
  color: var(--sb-cyan);
  text-shadow: 0 0 10px rgba(0, 229, 255, 0.35);
  white-space: nowrap;
  background: linear-gradient(180deg, rgba(0, 229, 255, 0.1), rgba(0, 229, 255, 0.02));
  border-bottom: 1px solid rgba(0, 229, 255, 0.3);
}
.sb-tbl tbody td {
  padding: 11px 10px;
  color: var(--sb-text-2);
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  white-space: nowrap;
}
.sb-tbl tbody tr {
  transition: background 0.15s var(--sb-ease);
}
.sb-tbl tbody tr:hover {
  background: rgba(0, 229, 255, 0.05);
}
.sb-tbl tbody tr:hover td {
  color: var(--sb-text);
}

@media (prefers-reduced-motion: reduce) {
  .sb-tbl tbody tr {
    transition: none;
  }
}
</style>
