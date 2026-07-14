<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import { useNonconformanceReport } from '@/composables/useBusinessNonconformanceReport'
import { NvAppShellMobile, NvCell, NvCellGroup, NvMobileButton, NvMobileTag } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '不合格报告',
  },
})

const route = useRoute('/quality/ncr/[ncrId]')
const router = useRouter()

const ncrId = computed(() => String(route.params.ncrId ?? ''))
// 来源检验记录 id（结果页跳转时带上），用于「返回检验记录」回链。
const fromRecordId = computed(() => {
  const v = route.query.from
  return typeof v === 'string' && v ? v : null
})

const { ncr, pending, error, refresh } = useNonconformanceReport(ncrId)

const statusLabel = computed(() => {
  switch (ncr.value?.status) {
    case 'open':
      return '待处置'
    case 'disposition-submitted':
      return '处置待审'
    case 'closed':
      return '已关闭'
    default:
      return ncr.value?.status ?? ''
  }
})

function goBack() {
  // 优先返回来路（本次检验流程）；无历史则回检验任务工作台。
  if (window.history.length > 1) router.back()
  else router.push('/quality/tasks').catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <NvMobileButton variant="text" size="sm" aria-label="返回" @click="goBack">返回</NvMobileButton>
        <h1 class="text-lg font-semibold text-foreground">不合格报告</h1>
      </div>
    </template>

    <div class="space-y-4 p-4">
      <RetryableListError
        v-if="error"
        :error="error"
        :pending="pending"
        fallback="不合格报告加载失败，请稍后重试。"
        test-id="ncr-error"
        @retry="() => refresh()"
      />

      <div v-else-if="pending" class="px-4 py-8 text-center text-sm text-muted-foreground">
        加载中…
      </div>

      <template v-else-if="ncr">
        <section class="space-y-2 rounded-lg border border-border bg-card p-4" data-testid="ncr-detail">
          <div class="flex items-center justify-between gap-2">
            <p class="text-base font-semibold text-foreground">{{ ncr.code }}</p>
            <NvMobileTag :variant="ncr.status === 'closed' ? 'default' : 'warning'">
              {{ statusLabel }}
            </NvMobileTag>
          </div>
          <p class="text-sm text-muted-foreground">检验不合格已自动发起不合格处置，请在处置流程中跟进。</p>
        </section>

        <NvCellGroup>
          <NvCell v-if="ncr.skuCode" title="物料" :value="ncr.skuCode" />
          <NvCell v-if="ncr.sourceDocumentId" title="来源单据" :value="ncr.sourceDocumentId" />
          <NvCell v-if="ncr.defectReason" title="不良原因" :value="ncr.defectReason" />
          <NvCell v-if="ncr.defectQuantity != null" title="不良数" :value="ncr.defectQuantity" />
          <NvCell v-if="ncr.batchNo" title="批次" :value="ncr.batchNo" />
          <NvCell v-if="ncr.serialNo" title="序列号" :value="ncr.serialNo" />
          <NvCell
            v-if="fromRecordId"
            data-testid="back-to-record"
            title="来源检验记录"
            :value="fromRecordId"
            arrow
            @click="goBack"
          />
        </NvCellGroup>

        <NvMobileButton variant="outline" size="lg" block data-testid="ncr-back" @click="goBack">
          返回检验流程
        </NvMobileButton>
      </template>
    </div>
  </NvAppShellMobile>
</template>
