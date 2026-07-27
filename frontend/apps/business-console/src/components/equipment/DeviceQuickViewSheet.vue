<script setup lang="ts">
import {
  describeEquipmentReason,
  useBusinessEquipmentDevice,
} from '@/composables/useBusinessEquipment'
import { friendlyErrorMessage } from '@/utils/notify'
import { alarmSeverityLabel, equipmentStateLabel } from '@nerv-iip/business-core'
import {
  NvBadge,
  NvButton,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
} from '@nerv-iip/ui'
import { ActivityIcon, GaugeIcon } from '@lucide/vue'
import { computed, toRef, watch } from 'vue'
import { RouterLink } from 'vue-router'

/**
 * 设备速览抽屉：看板行内「查看详情」的轻量下钻，不离开看板即可核对
 * 设备当前状态、未解除报警与最近可用性窗口（数据来自设备详情 facade，
 * 与完整详情页同源）。趋势曲线等重内容留给完整详情页，不在抽屉里伪造。
 */
const props = defineProps<{ deviceAssetId: string }>()
const open = defineModel<boolean>('open', { required: true })

const deviceAssetId = toRef(props, 'deviceAssetId')
const { activeAlarms, availabilityWindows, device, deviceError, devicePending, filters } =
  useBusinessEquipmentDevice(props.deviceAssetId)

watch(deviceAssetId, (value) => {
  filters.deviceAssetId = value
})

const currentState = computed(() => device.value?.currentState)
const stateText = computed(() => {
  const state = currentState.value?.currentState?.trim()
  // 设备没上报状态不是"未知状态"，而是当前没有实时数据可读——照实说。
  return state ? equipmentStateLabel(state) : '暂无实时数据'
})
const errorMessage = computed(() =>
  deviceError.value
    ? friendlyErrorMessage(deviceError.value, '设备信息加载失败，请稍后重试。')
    : '',
)
const recentAlarms = computed(() => activeAlarms.value.slice(0, 5))
const recentWindows = computed(() => availabilityWindows.value.slice(0, 5))
const unavailableWindowCount = computed(
  () =>
    availabilityWindows.value.filter(
      (window) => window.availabilityStatus?.toLowerCase() === 'unavailable',
    ).length,
)

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
</script>

<template>
  <NvSheet v-model:open="open">
    <NvSheetContent class="flex w-full flex-col gap-0 overflow-y-auto sm:max-w-lg">
      <NvSheetHeader>
        <NvSheetTitle>{{ deviceAssetId }}</NvSheetTitle>
        <NvSheetDescription
          >设备运行速览：当前状态、未解除报警与最近可用性窗口。</NvSheetDescription
        >
      </NvSheetHeader>

      <div class="grid gap-4 px-4 pb-4">
        <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>
        <p
          v-else-if="devicePending && !device"
          class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
          role="status"
        >
          正在加载设备信息…
        </p>

        <template v-else>
          <section class="grid gap-2 rounded-lg border p-3">
            <h3 class="text-xs font-medium text-muted-foreground">当前状态</h3>
            <div class="flex flex-wrap items-center gap-2">
              <NvBadge class="rounded-sm" variant="neutral">{{ stateText }}</NvBadge>
              <NvBadge
                class="rounded-sm"
                :variant="currentState?.isSourceFresh ? 'success' : 'warning'"
              >
                {{ currentState?.isSourceFresh ? '实时' : '暂无实时数据' }}
              </NvBadge>
            </div>
            <p class="text-xs text-muted-foreground">
              状态时间：{{ formatDateTime(currentState?.stateOccurredAtUtc) }}
            </p>
          </section>

          <section class="grid gap-2 rounded-lg border p-3">
            <div class="flex items-center justify-between">
              <h3 class="text-xs font-medium text-muted-foreground">未解除报警</h3>
              <NvBadge
                class="rounded-sm"
                :variant="recentAlarms.length > 0 ? 'danger' : 'neutral'"
                >{{ activeAlarms.length }}</NvBadge
              >
            </div>
            <ul v-if="recentAlarms.length" class="grid gap-2">
              <li
                v-for="alarm in recentAlarms"
                :key="alarm.alarmEventId ?? `${alarm.alarmCode}-${alarm.raisedAtUtc}`"
                class="flex flex-wrap items-center justify-between gap-2 text-sm"
              >
                <span class="font-medium text-foreground">{{
                  alarm.alarmCode ?? '未记录报警码'
                }}</span>
                <span class="flex items-center gap-2 text-xs text-muted-foreground">
                  <NvBadge class="rounded-sm" variant="danger">{{
                    alarmSeverityLabel(alarm.severity)
                  }}</NvBadge>
                  {{ formatDateTime(alarm.raisedAtUtc) }}
                </span>
              </li>
            </ul>
            <p v-else class="text-sm text-muted-foreground">当前没有未解除的报警。</p>
          </section>

          <section class="grid gap-2 rounded-lg border p-3">
            <div class="flex items-center justify-between">
              <h3 class="text-xs font-medium text-muted-foreground">最近可用性窗口</h3>
              <span class="text-xs text-muted-foreground"
                >不可用 {{ unavailableWindowCount }} / {{ availabilityWindows.length }} 段</span
              >
            </div>
            <ul v-if="recentWindows.length" class="grid gap-2">
              <li
                v-for="window in recentWindows"
                :key="`${window.reasonCode}-${window.startUtc}`"
                class="grid gap-1 text-sm"
              >
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <span class="font-medium text-foreground">{{
                    describeEquipmentReason(window.reasonCode ?? '').label
                  }}</span>
                  <NvBadge
                    class="rounded-sm"
                    :variant="
                      window.availabilityStatus?.toLowerCase() === 'unavailable'
                        ? 'danger'
                        : 'success'
                    "
                  >
                    {{
                      window.availabilityStatus?.toLowerCase() === 'unavailable' ? '不可用' : '可用'
                    }}
                  </NvBadge>
                </div>
                <p class="text-xs text-muted-foreground">
                  {{ formatDateTime(window.startUtc) }} — {{ formatDateTime(window.endUtc) }}
                </p>
              </li>
            </ul>
            <p v-else class="text-sm text-muted-foreground">当前没有可用性窗口记录。</p>
          </section>
        </template>
      </div>

      <NvSheetFooter class="mt-auto flex-row flex-wrap gap-2">
        <NvButton size="sm" type="button" variant="default" as-child>
          <RouterLink :to="`/equipment/${deviceAssetId}`">
            <ActivityIcon aria-hidden="true" />
            打开完整详情页
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="{ path: '/equipment/telemetry/oee', query: { deviceAssetId } }">
            <GaugeIcon aria-hidden="true" />
            OEE 与可用性
          </RouterLink>
        </NvButton>
      </NvSheetFooter>
    </NvSheetContent>
  </NvSheet>
</template>
