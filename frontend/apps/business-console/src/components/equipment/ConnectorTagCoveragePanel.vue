<script setup lang="ts">
import type { BusinessConsoleConnectorTagCoverageItem } from '@nerv-iip/api-client'
import { NvBadge, NvButton } from '@nerv-iip/ui'
import { RefreshCwIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed } from 'vue'

import { useBusinessTelemetryConnectorCoverage } from '@/composables/useBusinessTelemetry'

const props = defineProps<{
  collectionConnectorId: string
}>()

const connectorId = computed(() => props.collectionConnectorId)
const { coverage, coverageError, coveragePending, refreshCoverage } =
  useBusinessTelemetryConnectorCoverage(connectorId)

const items = computed(() => coverage.value?.items ?? [])
const manifestUnavailable = computed(() => coverage.value?.manifestStatus === 'unavailable')
const currentManifestEmpty = computed(
  () =>
    coverage.value?.manifestStatus === 'current' &&
    (coverage.value.configuredCount ?? items.value.length) === 0,
)

function formatDateTime(value?: string | null) {
  if (!value) return '尚未收到'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function itemState(item: BusinessConsoleConnectorTagCoverageItem) {
  if (!item.enabled || item.activationStatus === 'disabled') {
    return {
      label: '已停用',
      description: '此标签未启用采集',
      variant: 'neutral' as const,
    }
  }
  if (item.activationStatus === 'error') {
    return {
      label: '启用失败',
      description: '采集程序未能启用此标签',
      variant: 'danger' as const,
    }
  }
  if (item.activationStatus === 'active') {
    return {
      label: '已启用',
      description: '采集程序已启用此标签',
      variant: 'success' as const,
    }
  }
  return {
    label: '正在启用',
    description: '等待采集程序确认',
    variant: 'neutral' as const,
  }
}

function sampleDescription(item: BusinessConsoleConnectorTagCoverageItem) {
  if (item.lastSampleAtUtc) return `最近采样 ${formatDateTime(item.lastSampleAtUtc)}`
  if (item.activationStatus === 'active') return '等待首条数据'
  return '尚未收到采样'
}
</script>

<template>
  <section class="mt-3 border-t pt-3" aria-label="已配置采集标签">
    <div class="flex flex-wrap items-start justify-between gap-2">
      <div>
        <h3 class="text-sm font-semibold text-foreground">已配置采集标签</h3>
        <p
          v-if="coverage?.manifestStatus === 'current'"
          class="mt-0.5 text-[11px] text-muted-foreground"
        >
          已配置 {{ coverage.configuredCount ?? items.length }} · 已启用
          {{ coverage.enabledCount ?? 0 }} · 已激活 {{ coverage.activeCount ?? 0 }} · 已收到数据
          {{ coverage.everSampledCount ?? 0 }} · 启用失败 {{ coverage.errorCount ?? 0 }}
        </p>
      </div>
    </div>

    <div
      v-if="coveragePending"
      class="mt-3 rounded-md border border-dashed p-3 text-muted-foreground"
    >
      正在加载已配置标签…
    </div>

    <div v-else-if="coverageError" class="mt-3 rounded-md border border-destructive/40 p-3">
      <div class="flex items-start justify-between gap-3">
        <p class="inline-flex items-center gap-1.5 text-destructive">
          <TriangleAlertIcon class="size-3.5" aria-hidden="true" />
          已配置标签加载失败，请稍后重试。
        </p>
        <NvButton
          data-testid="coverage-retry"
          size="sm"
          type="button"
          variant="outline"
          @click="refreshCoverage"
        >
          <RefreshCwIcon aria-hidden="true" />重试
        </NvButton>
      </div>
    </div>

    <div
      v-else-if="manifestUnavailable"
      class="mt-3 rounded-md border border-dashed p-3 text-muted-foreground"
    >
      尚未上报已配置标签清单。请确认现场采集程序已升级并启用配置上报。
    </div>

    <div
      v-else-if="currentManifestEmpty"
      class="mt-3 rounded-md border border-dashed p-3 text-muted-foreground"
    >
      当前未配置采集标签。请在现场采集程序中完成标签映射后再查看。
    </div>

    <div v-else-if="items.length" class="mt-3 grid gap-2">
      <article
        v-for="item in items"
        :key="`${item.deviceAssetId ?? ''}:${item.tagKey ?? ''}`"
        class="rounded-md border bg-background px-3 py-2.5"
      >
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0">
            <p class="truncate font-medium text-foreground">{{ item.tagKey || '未命名标签' }}</p>
            <p class="mt-0.5 truncate text-[11px] text-muted-foreground">
              设备 {{ item.deviceAssetId || '未关联' }}
            </p>
          </div>
          <NvBadge class="shrink-0 rounded-sm" :variant="itemState(item).variant">
            {{ itemState(item).label }}
          </NvBadge>
        </div>
        <p
          class="mt-1.5 text-[11px]"
          :class="item.activationStatus === 'error' ? 'text-destructive' : 'text-muted-foreground'"
        >
          {{ itemState(item).description }}
        </p>
        <p class="mt-1 text-[11px] text-muted-foreground">
          {{ sampleDescription(item) }}
        </p>
      </article>
    </div>

    <div v-else class="mt-3 rounded-md border border-dashed p-3 text-muted-foreground">
      暂未取得已配置标签信息。
    </div>
  </section>
</template>
