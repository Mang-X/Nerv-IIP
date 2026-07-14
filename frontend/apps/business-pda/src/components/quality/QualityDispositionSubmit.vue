<script setup lang="ts">
import { NvMobileButton, NvMobileInput } from '@nerv-iip/ui-mobile'

defineProps<{
  /** 判不合格且已有特性行 → 处置原因必填。 */
  dispositionRequired: boolean
  hasRows: boolean
  overallVerdict: 'pass' | 'fail' | 'pending'
  canSubmit: boolean
  submitPending: boolean
}>()
const emit = defineEmits<{ submit: [] }>()
const dispositionReason = defineModel<string>('dispositionReason', { default: '' })
</script>

<template>
  <!-- 处置原因（判不合格时必填）-->
  <label v-if="dispositionRequired && hasRows" class="block space-y-1">
    <span class="text-sm font-medium text-destructive">处置原因（不合格必填）</span>
    <NvMobileInput
      v-model="dispositionReason"
      data-testid="disposition-reason"
      placeholder="如 外径超差且外观不良，判退"
    />
  </label>

  <NvMobileButton
    :variant="overallVerdict === 'fail' ? 'danger' : 'primary'"
    size="lg"
    block
    data-testid="submit"
    :disabled="!canSubmit || submitPending"
    @click="emit('submit')"
  >
    {{
      submitPending ? '提交中…' : overallVerdict === 'fail' ? '提交（判不合格）' : '提交检验结果'
    }}
  </NvMobileButton>
</template>
