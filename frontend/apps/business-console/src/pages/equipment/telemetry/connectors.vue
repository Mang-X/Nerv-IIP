<script setup lang="ts">
import type { TelemetryConnectorConfigItem } from '@nerv-iip/api-client'
import { NvButton, NvPageHeader } from '@nerv-iip/ui'
import { computed, reactive, ref, watch } from 'vue'
import ConnectorConfigSheet from '@/components/equipment/ConnectorConfigSheet.vue'
import ConnectorHealthCard from '@/components/equipment/ConnectorHealthCard.vue'
import { useConnectorConfigs } from '@/composables/equipment/useConnectorConfigs'
import {
  connectorSourceSystemLabel,
  useBusinessTelemetryConnectors,
} from '@/composables/equipment/useBusinessTelemetry'
import { hasBusinessContext } from '@/composables/shared/businessContextBinding'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useAuthStore } from '@/stores/auth'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifyOperationFailure, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '连接与采集配置',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const context = useBusinessContextStore()
const contextReady = computed(() => hasBusinessContext(context))
const auth = useAuthStore()
const canManage = computed(() =>
  auth.principal?.permissionCodes?.includes('business.iiot.tags.manage'),
)
const { configs, configsError, configsPending, refreshConfigs, deleteConfig, saveConfig } =
  useConnectorConfigs()
const { connectors, connectorsError, connectorsPending, refreshConnectors, sampleRateByConnector } =
  useBusinessTelemetryConnectors()

const rows = computed(() => {
  const healthById = new Map(connectors.value.map((health) => [health.connectorId, health]))
  const configuredIds = new Set(configs.value.map((config) => config.connectorId))
  return [
    ...configs.value.map((config) => ({
      id: config.connectorId ?? '',
      config,
      health: healthById.get(config.connectorId),
    })),
    ...connectors.value
      .filter((health) => !configuredIds.has(health.connectorId))
      .map((health) => ({
        id: health.connectorId ?? '',
        config: undefined,
        health,
      })),
  ]
})
const error = computed(() => configsError.value ?? connectorsError.value)
watch(
  () => Boolean(error.value),
  (failed) => {
    if (failed) notifyError(error.value, '连接器读取失败，请稍后重试。')
  },
)
const expanded = reactive(new Set<string>())
function toggleDetails(id: string) {
  if (expanded.has(id)) expanded.delete(id)
  else expanded.add(id)
}
function refresh() {
  void refreshConfigs()
  void refreshConnectors()
}
const editing = ref<TelemetryConnectorConfigItem | null>(null)
const sheetOpen = ref(false)
const deleting = ref<TelemetryConnectorConfigItem>()
const busy = ref(false)

function edit(config: TelemetryConnectorConfigItem | null) {
  editing.value = config
  sheetOpen.value = true
}

async function change(config: TelemetryConnectorConfigItem, remove = false) {
  busy.value = true
  try {
    if (remove) {
      await deleteConfig(config.connectorId ?? '')
      deleting.value = undefined
    } else {
      await saveConfig({ ...config, isEnabled: !config.isEnabled })
    }
    notifySuccess(remove ? '连接器已删除' : '采集状态已更新')
  } catch (cause) {
    const action = remove ? '删除连接器失败' : '更新采集状态失败'
    notifyOperationFailure(action, cause, action)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="连接与采集配置"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="contextReady && !error ? `${rows.length} 个连接器` : undefined"
    >
      <template #actions>
        <NvButton size="sm" variant="outline" :disabled="!contextReady" @click="refresh"
          >刷新</NvButton
        >
        <NvButton
          v-if="canManage"
          :disabled="!contextReady"
          size="sm"
          variant="brand"
          @click="edit(null)"
          >新建连接器</NvButton
        >
      </template>
    </NvPageHeader>
    <p v-if="!contextReady" role="status" class="text-sm text-muted-foreground">
      尚未确定当前组织，暂未查询采集健康。
    </p>
    <div
      v-else-if="error"
      role="alert"
      class="flex items-center justify-between gap-3 rounded-lg border border-destructive/30 p-4 text-sm text-destructive"
    >
      <span>{{
        rows.length
          ? '连接器刷新失败，下方显示上一次读取的结果。'
          : '连接器读取失败，当前无法判断现场采集状态。'
      }}</span>
      <NvButton size="sm" variant="outline" @click="refresh">重试</NvButton>
    </div>
    <p
      v-else-if="configsPending || connectorsPending"
      role="status"
      class="text-sm text-muted-foreground"
    >
      正在读取连接器…
    </p>
    <p
      v-else-if="!rows.length"
      class="rounded-lg border border-dashed p-6 text-sm text-muted-foreground"
    >
      暂无连接器。
    </p>
    <div class="grid items-start gap-4 xl:grid-cols-2">
      <article
        v-for="{ id, config, health } in rows"
        :key="id"
        class="grid gap-3 rounded-lg border bg-card p-4"
      >
        <div v-if="config" class="grid gap-2">
          <div class="flex flex-wrap items-start justify-between gap-2">
            <div>
              <h2 class="font-semibold">{{ config.displayName }}</h2>
              <p class="text-xs text-muted-foreground">{{ id }}</p>
            </div>
            <div v-if="canManage" class="flex gap-1">
              <NvButton size="sm" variant="ghost" :disabled="busy" @click="edit(config)"
                >编辑</NvButton
              >
              <NvButton size="sm" variant="ghost" :disabled="busy" @click="change(config)">{{
                config.isEnabled ? '停用' : '启用'
              }}</NvButton>
              <NvButton size="sm" variant="ghost" :disabled="busy" @click="deleting = config"
                >删除</NvButton
              >
            </div>
          </div>
          <p class="text-sm">
            {{ connectorSourceSystemLabel(config.protocol) }} · {{ config.pollIntervalSeconds }} 秒
            · {{ config.mappings?.length ?? 0 }} 个测点 ·
            {{ config.isEnabled ? '已启用' : '已停用' }}
          </p>
          <p class="break-all font-mono text-xs text-muted-foreground">{{ config.endpoint }}</p>
          <div
            v-if="deleting?.connectorId === config.connectorId"
            class="flex flex-wrap items-center gap-2 text-sm"
          >
            删除连接器「{{ config.displayName }}」？
            <NvButton size="sm" variant="outline" :disabled="busy" @click="deleting = undefined"
              >取消</NvButton
            >
            <NvButton size="sm" variant="destructive" :disabled="busy" @click="change(config, true)"
              >确认删除</NvButton
            >
          </div>
        </div>
        <ConnectorHealthCard
          v-if="health"
          :connector="health"
          :sample-rate="sampleRateByConnector[id] ?? null"
          :expanded="expanded.has(id)"
          @toggle="toggleDetails(id)"
        />
        <p v-else class="text-sm text-muted-foreground">
          {{ connectorsError ? '运行态读取失败' : '暂无运行态上报' }}
        </p>
      </article>
    </div>
    <ConnectorConfigSheet
      v-if="canManage"
      v-model:open="sheetOpen"
      :config="editing"
      @saved="refreshConnectors"
    />
  </BusinessLayout>
</template>
