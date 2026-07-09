<script setup lang="ts">
import type { InstanceListItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { Button, NvDataTable, NvStatusBadge } from '@nerv-iip/ui'
import { instanceStatusLabel, instanceTone } from './instanceStatus'

const props = defineProps<{
  instances: InstanceListItem[]
  pending?: boolean
  restartPending?: boolean
  selectedInstanceKey?: string
}>()

const emit = defineEmits<{
  restartInstance: [instanceKey: string]
  selectInstance: [instanceKey: string]
}>()

type InstanceRow = InstanceListItem

const columns: NvDataTableColumn<InstanceRow>[] = [
  {
    key: 'app',
    header: '应用',
    accessor: (r) => r.applicationName ?? r.applicationKey ?? '未知应用',
  },
  {
    key: 'instance',
    header: '实例',
    accessor: (r) => r.instanceName ?? r.instanceKey ?? '未知实例',
  },
  { key: 'node', header: '节点', accessor: (r) => r.nodeName ?? r.nodeKey ?? '未分配' },
  { key: 'reportedStatus', header: '状态', width: 'w-24' },
  { key: 'healthStatus', header: '健康', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-40' },
]

function instanceLabel(instance: InstanceListItem) {
  return instance.instanceName ?? instance.instanceKey ?? '未知实例'
}

function rowKey(instance: InstanceListItem) {
  return instance.instanceKey ?? instance.instanceName ?? instance.applicationKey ?? '未知实例'
}
</script>

<template>
  <NvDataTable
    :pagination="false"
    :searchable="false"
    :column-settings="false"
    :columns="columns"
    :rows="props.instances"
    :row-key="rowKey"
    :loading="props.pending"
    empty-message="该环境暂无实例。"
  >
    <template #cell-app="{ row }">
      <div class="flex flex-col gap-0.5">
        <span class="font-semibold">{{
          row.applicationName ?? row.applicationKey ?? '未知应用'
        }}</span>
        <span class="text-xs text-muted-foreground">{{ row.version ?? '无版本' }}</span>
      </div>
    </template>
    <template #cell-reportedStatus="{ row }">
      <NvStatusBadge
        :label="instanceStatusLabel(row.reportedStatus)"
        :tone="instanceTone(row.reportedStatus)"
      />
    </template>
    <template #cell-healthStatus="{ row }">
      <NvStatusBadge
        :label="instanceStatusLabel(row.healthStatus)"
        :tone="instanceTone(row.healthStatus)"
      />
    </template>
    <template #cell-actions="{ row }">
      <div class="flex items-center justify-end gap-2">
        <Button
          size="sm"
          type="button"
          variant="ghost"
          :aria-label="`查看 ${instanceLabel(row)}`"
          :disabled="!row.instanceKey || row.instanceKey === props.selectedInstanceKey"
          @click="row.instanceKey && emit('selectInstance', row.instanceKey)"
        >
          查看
        </Button>
        <Button
          size="sm"
          type="button"
          variant="outline"
          :aria-label="`重启 ${instanceLabel(row)}`"
          :disabled="props.restartPending || !row.instanceKey"
          @click="row.instanceKey && emit('restartInstance', row.instanceKey)"
        >
          重启
        </Button>
      </div>
    </template>
  </NvDataTable>
</template>
