<script setup lang="ts">
import type { BusinessConsoleBarcodeResolveCandidate } from '@nerv-iip/api-client'
import { NvMobileButton, NvScanBar } from '@nerv-iip/ui-mobile'
import { computed, onBeforeUnmount, shallowRef } from 'vue'
import { isNavigationFailure, useRouter } from 'vue-router'

import { usePdaBarcodeResolver } from '@/composables/usePdaBarcodeResolver'
import { usePdaIdentity } from '@/composables/useWorkbenchHome'

const props = withDefaults(defineProps<{ active?: boolean }>(), { active: true })
const identity = usePdaIdentity()
const router = useRouter()
const resolver = usePdaBarcodeResolver({
  organizationId: identity.organizationId,
  environmentId: identity.environmentId,
})
const navigationStatus = shallowRef<'idle' | 'pending' | 'succeeded' | 'error'>('idle')
const navigationPending = computed(() => navigationStatus.value === 'pending')
const scanDisabled = navigationPending
let navigationGeneration = 0
onBeforeUnmount(() => {
  resolver.cancel()
  navigationGeneration += 1
})

const statusCopy = computed(() => {
  if (navigationStatus.value === 'error') return '无法打开目标页面，请重新扫码或稍后重试。'
  if (navigationStatus.value === 'pending') return '已确认唯一对象，正在直达…'
  if (navigationStatus.value === 'succeeded') return '目标页面已打开。'
  switch (resolver.status.value) {
    case 'pending':
      return '正在解析扫码内容…'
    case 'resolved':
      return '已确认唯一对象，正在直达…'
    case 'ambiguous':
      return '找到多个候选，请手动选择；系统不会猜测。'
    case 'unknown':
      return '无法确认该扫码内容。可查询当前权限范围内的服务端候选。'
    case 'unsupported':
      return '已识别，但当前 PDA 暂不支持直达该对象。'
    case 'forbidden':
      return '当前账号无权解析该扫码内容。'
    case 'error':
      return '解析服务暂不可用，请稍后重试。'
    default:
      return ''
  }
})

function candidateLabel(candidate: BusinessConsoleBarcodeResolveCandidate) {
  const labels: Record<string, string> = {
    'mes-work-order': '生产工单',
    'mes-operation': '工序任务',
    'equipment-device': '设备',
  }
  const strongIds = Object.values(candidate.strongIds ?? {})
    .filter(Boolean)
    .join(' · ')
  return `${labels[candidate.objectType ?? ''] ?? candidate.objectType ?? '未知对象'}${strongIds ? `：${strongIds}` : ''}`
}

async function navigate(route: ReturnType<typeof resolver.selectCandidate>) {
  if (!route || navigationPending.value) return
  const currentGeneration = ++navigationGeneration
  navigationStatus.value = 'pending'
  try {
    const failure = await router.push(route)
    if (currentGeneration !== navigationGeneration) return
    navigationStatus.value = isNavigationFailure(failure) ? 'error' : 'succeeded'
  } catch {
    if (currentGeneration === navigationGeneration) navigationStatus.value = 'error'
  }
}

async function onScan(value: string) {
  if (scanDisabled.value) return
  navigationGeneration += 1
  navigationStatus.value = 'idle'
  await navigate(await resolver.resolve(value))
}

async function onCandidate(candidate: BusinessConsoleBarcodeResolveCandidate) {
  if (navigationPending.value) return
  await navigate(resolver.selectCandidate(candidate))
}
</script>

<template>
  <div class="space-y-3">
    <fieldset :disabled="scanDisabled" class="m-0 min-w-0 border-0 p-0">
      <NvScanBar
        placeholder="扫描工单 / 工序 / 设备"
        :active="props.active && !scanDisabled"
        @scan="onScan"
      />
    </fieldset>

    <section
      v-if="statusCopy"
      data-testid="barcode-status"
      :role="
        navigationStatus === 'error' ||
        resolver.status.value === 'forbidden' ||
        resolver.status.value === 'error'
          ? 'alert'
          : 'status'
      "
      class="rounded-xl border border-border bg-card p-4"
      aria-live="polite"
    >
      <p class="text-sm font-medium text-foreground">{{ statusCopy }}</p>
      <p
        v-if="resolver.scannedValue.value"
        class="mt-1 break-all font-mono text-xs text-muted-foreground"
      >
        {{ resolver.scannedValue.value }}
      </p>
    </section>

    <div v-if="resolver.status.value === 'ambiguous'" class="space-y-2">
      <NvMobileButton
        v-for="(candidate, index) in resolver.candidates.value"
        :key="`${candidate.objectType}-${index}`"
        :data-testid="`barcode-candidate-${index}`"
        variant="outline"
        size="lg"
        block
        :disabled="navigationPending"
        @click="onCandidate(candidate)"
      >
        {{ candidateLabel(candidate) }}
      </NvMobileButton>
    </div>

    <div v-if="resolver.status.value === 'unknown'" class="space-y-3">
      <NvMobileButton
        data-testid="barcode-search"
        variant="outline"
        size="lg"
        block
        :disabled="resolver.searchStatus.value === 'pending'"
        @click="resolver.searchUnknownCandidates"
      >
        {{ resolver.searchStatus.value === 'pending' ? '正在查询候选…' : '查询服务端候选' }}
      </NvMobileButton>

      <section
        v-if="resolver.searchStatus.value === 'resolved'"
        data-testid="barcode-search-results"
        class="rounded-xl border border-border bg-card p-4"
      >
        <h2 class="text-sm font-medium text-foreground">仅供核对的候选（未验证主数据）</h2>
        <p
          v-if="resolver.searchResults.value.length === 0"
          class="mt-2 text-sm text-muted-foreground"
        >
          当前权限范围内没有候选。
        </p>
        <ul v-else class="mt-2 space-y-2">
          <li
            v-for="(result, index) in resolver.searchResults.value"
            :key="`${result.objectType}-${result.objectNumber}-${index}`"
            class="rounded-lg bg-muted p-3"
          >
            <p class="text-sm font-medium text-foreground">
              {{ result.title || result.objectNumber }}
            </p>
            <p class="text-xs text-muted-foreground">
              {{ result.objectType
              }}<template v-if="result.objectNumber"> · {{ result.objectNumber }}</template>
            </p>
          </li>
        </ul>
      </section>

      <p
        v-else-if="
          resolver.searchStatus.value === 'forbidden' || resolver.searchStatus.value === 'error'
        "
        role="alert"
        class="text-sm text-destructive"
      >
        {{ resolver.searchStatus.value === 'forbidden' ? '无权查询候选。' : '候选查询暂不可用。' }}
      </p>
    </div>
  </div>
</template>
