<script setup lang="ts">
import { useBusinessDeviceDirectory } from '@/composables/useBusinessDeviceDirectory'
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import { NvBottomSheet, NvListRow, NvMobileButton, NvSearchBar } from '@nerv-iip/ui-mobile'
import { computed, ref, watch } from 'vue'

type SelectableDeviceAsset = BusinessConsoleResourceItem & { deviceAssetId: string }

const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ select: [device: SelectableDeviceAsset] }>()
const searchKeyword = ref('')

const {
  deviceAssets,
  deviceAssetsTotal,
  deviceAssetsPending,
  deviceAssetsError,
  deviceAssetFilters,
  scopeReady,
  canPreviousPage,
  canNextPage,
  search,
  previousPage,
  nextPage,
  refreshDeviceAssets,
} = useBusinessDeviceDirectory()

const pageNumber = computed(() => Math.floor(deviceAssetFilters.skip / deviceAssetFilters.take) + 1)
const pageCount = computed(() =>
  Math.max(1, Math.ceil(deviceAssetsTotal.value / deviceAssetFilters.take)),
)

watch(open, (isOpen) => {
  if (isOpen) {
    searchKeyword.value = ''
    search('')
  }
})

function deviceTitle(device: SelectableDeviceAsset) {
  return device.displayName?.trim() || device.code?.trim() || device.deviceAssetId
}

function deviceSubtitle(device: SelectableDeviceAsset) {
  const title = deviceTitle(device)
  const context = [
    device.code?.trim() !== title ? device.code?.trim() : undefined,
    device.siteCode,
    device.plantCode,
    device.workshopCode,
    device.lineCode,
    device.workCenterCode,
    device.stationCode,
  ]
    .filter((part): part is string => Boolean(part?.trim()))
    .filter((part, index, parts) => parts.indexOf(part) === index)
  return context.join(' · ')
}

function selectDevice(device: SelectableDeviceAsset) {
  emit('select', device)
  open.value = false
}

function clearSearch() {
  searchKeyword.value = ''
  search('')
}
</script>

<template>
  <NvBottomSheet
    :open="open"
    title="选择设备"
    description="按设备名称或编码搜索"
    @update:open="open = $event"
  >
    <div class="space-y-3 pb-2">
      <NvSearchBar
        v-model="searchKeyword"
        cancelable
        placeholder="搜索设备名称 / 编码"
        @search="search"
        @cancel="clearSearch"
      />

      <div
        v-if="!scopeReady"
        class="rounded-lg border border-dashed border-border px-4 py-8 text-center text-sm text-muted-foreground"
      >
        登录范围尚未就绪
      </div>
      <div
        v-else-if="deviceAssetsPending"
        class="rounded-lg border border-border px-4 py-8 text-center text-sm text-muted-foreground"
      >
        正在加载设备…
      </div>
      <div
        v-else-if="deviceAssetsError"
        role="alert"
        class="space-y-3 rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm"
      >
        <p class="text-destructive">设备加载失败，请稍后重试。</p>
        <NvMobileButton
          data-testid="device-retry"
          variant="outline"
          size="lg"
          block
          @click="refreshDeviceAssets"
        >
          重试
        </NvMobileButton>
      </div>
      <div
        v-else-if="deviceAssets.length === 0"
        class="rounded-lg border border-dashed border-border px-4 py-8 text-center text-sm text-muted-foreground"
      >
        没有找到可选设备
      </div>
      <div v-else class="max-h-[48vh] overflow-y-auto rounded-lg border border-border">
        <NvListRow
          v-for="device in deviceAssets"
          :key="device.deviceAssetId"
          :data-testid="`device-option-${device.deviceAssetId}`"
          :title="deviceTitle(device)"
          :subtitle="deviceSubtitle(device)"
          @select="selectDevice(device)"
        />
      </div>

      <div v-if="scopeReady && !deviceAssetsError" class="flex items-center gap-2">
        <NvMobileButton
          data-testid="device-previous-page"
          variant="outline"
          size="lg"
          class="flex-1"
          :disabled="!canPreviousPage"
          @click="previousPage"
        >
          上一页
        </NvMobileButton>
        <span class="shrink-0 text-sm text-muted-foreground">
          {{ pageNumber }} / {{ pageCount }}
        </span>
        <NvMobileButton
          data-testid="device-next-page"
          variant="outline"
          size="lg"
          class="flex-1"
          :disabled="!canNextPage"
          @click="nextPage"
        >
          下一页
        </NvMobileButton>
      </div>
    </div>
  </NvBottomSheet>
</template>
