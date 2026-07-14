<script setup lang="ts">
import type { QualityResultState } from '@/composables/useInspectionExecution'
import { NvCell, NvMobileButton, NvMobileResult } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

const props = defineProps<{ state: QualityResultState }>()
const emit = defineEmits<{ next: []; back: []; retry: []; openNcr: [] }>()

// 后端权威结论口径：passed 合格 / rejected 不合格 / conditional-release 条件放行。
const passed = computed(
  () => props.state.phase === 'submitted' && props.state.authoritative.result === 'passed',
)
const ncrId = computed(() =>
  props.state.phase === 'submitted' ? props.state.authoritative.nonconformanceReportId : null,
)
// 互查用人读单号（后端 NcrCode）；仅有 id 无 code 时回退展示 id。
const ncrCode = computed(() =>
  props.state.phase === 'submitted' ? props.state.authoritative.nonconformanceReportCode : null,
)
const ncrDisplay = computed(() => ncrCode.value || ncrId.value || '')
const resultTitle = computed(() => {
  if (props.state.phase !== 'submitted') return '提交失败'
  switch (props.state.authoritative.result) {
    case 'passed':
      return '检验合格'
    case 'conditional-release':
      return '条件放行'
    case 'rejected':
      return '检验不合格'
    default:
      return '已提交'
  }
})
const resultDescription = computed(() => {
  if (props.state.phase !== 'submitted') return props.state.message
  if (passed.value) return '检验结果已记录。'
  // 非合格：后端已在同事务内自动开出 NCR 并回链（真实闭环）。
  return ncrId.value
    ? '已自动发起 NCR 处置，请在不合格处置流程中跟进。'
    : '检验结果已记录，请按处置流程跟进。'
})
</script>

<template>
  <!-- 合格 → 绿色；不合格 / 条件放行 → 红色（error 呈现）；提交失败 → 红色 -->
  <NvMobileResult
    :status="passed ? 'success' : 'error'"
    :title="resultTitle"
    :description="resultDescription"
  >
    <template #actions>
      <!-- NCR 互链入口：展示人读单号（NcrCode），点按打开该 NCR 详情。 -->
      <NvCell
        v-if="state.phase === 'submitted' && ncrDisplay"
        data-testid="ncr-link"
        class="w-full overflow-hidden rounded-lg border border-border"
        arrow
        title="不合格报告单号"
        note="点按查看不合格报告"
        @click="emit('openNcr')"
      >
        <template #value>
          <span class="max-w-[9rem] truncate font-medium text-foreground">{{ ncrDisplay }}</span>
        </template>
      </NvCell>

      <NvMobileButton
        v-if="state.phase === 'submitted'"
        variant="primary"
        size="lg"
        block
        data-testid="next-task"
        @click="emit('next')"
      >
        下一个任务
      </NvMobileButton>
      <NvMobileButton
        v-else
        variant="primary"
        size="lg"
        block
        data-testid="retry-submit"
        @click="emit('retry')"
      >
        重试
      </NvMobileButton>
      <NvMobileButton variant="outline" size="lg" block data-testid="result-back" @click="emit('back')">
        {{ state.phase === 'submitted' ? '返回工作台' : '返回' }}
      </NvMobileButton>
    </template>
  </NvMobileResult>
</template>
