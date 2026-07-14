<script setup lang="ts">
import QualityInspectionRecordDetail from '@/components/quality/QualityInspectionRecordDetail.vue'
import { useInspectionRecordDetail } from '@/composables/useBusinessInspectionRecord'
import { NvAppShellMobile, NvMobileButton } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '检验记录',
  },
})

const route = useRoute('/quality/record/[recordId]')
const router = useRouter()

const recordId = computed(() => String(route.params.recordId ?? ''))
const { record, pending, error, refresh } = useInspectionRecordDetail(recordId)

function goBack() {
  // 优先返回来路（NCR 详情 / 检验流程）；无历史则回检验任务工作台。
  if (window.history.length > 1) router.back()
  else router.push('/quality/tasks').catch(() => {})
}
function openNcr(ncrId: string) {
  // 记录 → NCR 互链（带上本记录 id，NCR 页可回链）。
  router
    .push({ path: `/quality/ncr/${ncrId}`, query: { from: recordId.value } })
    .catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <NvMobileButton variant="text" size="sm" aria-label="返回" @click="goBack">返回</NvMobileButton>
        <h1 class="text-lg font-semibold text-foreground">检验记录</h1>
      </div>
    </template>

    <QualityInspectionRecordDetail
      :record="record"
      :pending="pending"
      :error="error"
      @retry="() => refresh()"
      @back="goBack"
      @open-ncr="openNcr"
    />
  </NvAppShellMobile>
</template>
