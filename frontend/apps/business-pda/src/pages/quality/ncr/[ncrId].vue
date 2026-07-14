<script setup lang="ts">
import QualityNcrDetail from '@/components/quality/QualityNcrDetail.vue'
import { useNonconformanceReport } from '@/composables/useBusinessNonconformanceReport'
import { NvAppShellMobile, NvMobileButton } from '@nerv-iip/ui-mobile'
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

// 来源检验记录互链取服务端权威字段（响应里的 sourceInspectionRecordId），不读客户端 query。
const { ncr, pending, error, refresh } = useNonconformanceReport(ncrId)

function goBack() {
  // 优先返回来路（本次检验流程）；无历史则回检验任务工作台。
  if (window.history.length > 1) router.back()
  else router.push('/quality/tasks').catch(() => {})
}
function openRecord(recordId: string) {
  // NCR → 检验记录互链（真实路由；记录页可再跳回本 NCR）。
  router.push(`/quality/record/${recordId}`).catch(() => {})
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

    <QualityNcrDetail
      :ncr="ncr"
      :pending="pending"
      :error="error"
      @retry="() => refresh()"
      @back="goBack"
      @open-record="openRecord"
    />
  </NvAppShellMobile>
</template>
