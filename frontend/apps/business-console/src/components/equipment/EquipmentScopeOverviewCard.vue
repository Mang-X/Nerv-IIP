<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { NvBadge, NvButton, NvDataTable } from '@nerv-iip/ui'
import { ArrowRightIcon } from '@lucide/vue'
import { computed } from 'vue'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'

/**
 * 设备域四页共用的「范围设备总览」聚合视图：未下钻到单台设备时，
 * 列出当前范围（全厂 / 车间 / 产线）内的设备主数据，点某台即下钻到
 * 该页的单设备视图。只展示主数据真实字段，不为每台设备并发打接口
 * 伪造范围级指标。
 */
const props = defineProps<{
  devices: BusinessConsoleResourceItem[]
  scopeLabel: string
  pending?: boolean
  /** 行内下钻按钮文案（如「查看趋势」「查看 OEE」）。 */
  actionLabel: string
  /** 范围说明（放在标题下，说明选中设备后会看到什么）。 */
  description: string
}>()

const emit = defineEmits<{ (e: 'select', code: string): void }>()

// 车间 / 产线在设备台账上只有编码，中文名在各自的主数据目录里，走统一名录解析 join 出来。
const { resolveWorkshop, resolveLine } = useMasterDataDisplayNames({
  workshops: true,
  lines: true,
})
/** 目录查不到就只显编码，不编名字。 */
function scopeName(resolve: (code?: string | null) => string | undefined, code?: string | null) {
  if (!code) return '未划分'
  return resolve(code) ?? code
}

/** 「名称 编号」串，供排序与导出用；没登记名称就只有编号，不编名字。 */
function deviceText(item: BusinessConsoleResourceItem) {
  const name = item.displayName?.trim()
  return name ? `${name} ${item.code ?? ''}`.trim() : (item.code ?? '无编号')
}

const columns: NvDataTableColumn<BusinessConsoleResourceItem>[] = [
  // 名称在上、编号在下：现场先认名字，编号是核对用的次要信息。
  {
    key: 'code',
    header: '设备',
    cellClass: 'font-medium',
    accessor: (r) => deviceText(r),
  },
  {
    key: 'workshopCode',
    header: '车间',
    width: 'w-32',
    accessor: (r) => scopeName(resolveWorkshop, r.workshopCode),
  },
  {
    key: 'lineCode',
    header: '产线',
    width: 'w-32',
    accessor: (r) => scopeName(resolveLine, r.lineCode),
  },
  { key: 'active', header: '台账状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-32' },
]

function select(code?: string | null) {
  const normalized = (code ?? '').trim()
  if (normalized) emit('select', normalized)
}
</script>

<template>
  <section class="grid gap-3" aria-label="范围设备总览">
    <div>
      <h2 class="text-base font-semibold text-foreground">{{ props.scopeLabel }} · 范围设备</h2>
      <p class="mt-1 text-sm text-muted-foreground">{{ props.description }}</p>
    </div>
    <NvDataTable
      :columns="columns"
      :rows="props.devices"
      :row-key="(r) => r.code ?? '无编号'"
      :loading="props.pending"
      :searchable="false"
      :column-settings="false"
      empty-message="范围内暂无设备主数据。请调整上方范围，或先在基础数据登记设备资产。"
      @row-click="(row) => select(row.code)"
    >
      <template #cell-code="{ row }">
        <span class="grid leading-tight">
          <span>{{ row.displayName?.trim() ? row.displayName : (row.code ?? '无编号') }}</span>
          <span v-if="row.displayName?.trim() && row.code" class="text-xs text-muted-foreground">{{
            row.code
          }}</span>
        </span>
      </template>
      <template #cell-active="{ row }">
        <NvBadge class="rounded-sm" :variant="row.active === false ? 'neutral' : 'success'">
          {{ row.active === false ? '已停用' : '启用中' }}
        </NvBadge>
      </template>
      <template #cell-actions="{ row }">
        <NvButton size="sm" type="button" variant="outline" @click.stop="select(row.code)">
          {{ props.actionLabel }}
          <ArrowRightIcon aria-hidden="true" />
        </NvButton>
      </template>
    </NvDataTable>
  </section>
</template>
