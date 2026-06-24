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
  font-size: 14px;
  font-variant-numeric: tabular-nums;
}
.sb-tbl th {
  position: sticky;
  top: 0;
  padding: 12px 14px;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.02em;
  color: var(--sb-text-2);
  white-space: nowrap;
  background: #0e1626;
  border-bottom: 1px solid var(--sb-line-2);
}
.sb-tbl tbody td {
  padding: 13px 14px;
  color: var(--sb-text-2);
  border-bottom: 1px solid var(--sb-line);
  white-space: nowrap;
}
/* zebra — every other row lifts a hair for legibility at a distance */
.sb-tbl tbody tr:nth-child(even) td {
  background: rgba(255, 255, 255, 0.018);
}
.sb-tbl tbody tr:last-child td {
  border-bottom: 0;
}
.sb-tbl tbody tr {
  transition: background 0.15s var(--sb-ease);
}
.sb-tbl tbody tr:hover td {
  background: rgba(67, 180, 228, 0.08);
  color: var(--sb-text);
}

@media (prefers-reduced-motion: reduce) {
  .sb-tbl tbody tr {
    transition: none;
  }
}
</style>
