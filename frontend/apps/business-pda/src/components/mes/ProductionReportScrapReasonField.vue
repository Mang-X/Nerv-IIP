<script setup lang="ts">
import { computed } from 'vue'

interface ScrapReasonOption {
  reasonCode?: string | null
  reasonName?: string | null
  enabled?: boolean | null
}

const props = defineProps<{
  modelValue: string
  qualityInspectionRecordsReadPermission: boolean
  scrapReasonCodesPending: boolean
  scrapReasonCodesError: unknown
  scrapReasonCodes: readonly ScrapReasonOption[]
  scrapReasonValidationMessage: string
  submitting: boolean
  reportScopeReady: boolean
  refreshScrapReasonCodes: () => unknown
}>()

const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const hasError = computed(() => Boolean(props.scrapReasonCodesError))

function updateReasonCode(event: Event) {
  emit('update:modelValue', (event.target as HTMLSelectElement).value)
}
</script>

<template>
  <section
    data-testid="scrap-reason-code-field"
    class="space-y-2 rounded-lg border border-border bg-card p-3"
  >
    <div class="flex items-center justify-between gap-3">
      <label for="scrap-reason-code" class="text-sm font-medium text-foreground">
        报废原因码 <span class="text-destructive">*</span>
      </label>
      <button
        type="button"
        class="text-sm text-primary"
        :disabled="scrapReasonCodesPending || submitting"
        @click="refreshScrapReasonCodes"
      >
        刷新
      </button>
    </div>
    <select
      id="scrap-reason-code"
      :value="modelValue"
      data-testid="scrap-reason-code"
      class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base outline-none focus:border-primary disabled:opacity-60"
      :disabled="
        !qualityInspectionRecordsReadPermission ||
        scrapReasonCodesPending ||
        hasError ||
        submitting ||
        !reportScopeReady
      "
      @change="updateReasonCode"
    >
      <option value="">请选择报废原因码</option>
      <option
        v-for="reason in scrapReasonCodes"
        :key="reason.reasonCode ?? ''"
        :value="reason.reasonCode ?? ''"
      >
        {{ reason.reasonCode }} · {{ reason.reasonName }}
      </option>
    </select>
    <p v-if="scrapReasonCodesError" class="text-sm text-destructive" role="alert">
      报废原因码读取失败，请刷新后重试。
    </p>
    <p v-else-if="scrapReasonCodesPending" class="text-sm text-muted-foreground">
      正在读取报废原因码…
    </p>
    <p v-else-if="!qualityInspectionRecordsReadPermission" class="text-sm text-muted-foreground">
      当前账号没有质量原因码读取权限，报废报工已禁用。
    </p>
    <p v-else-if="scrapReasonValidationMessage" class="text-sm text-destructive" role="alert">
      {{ scrapReasonValidationMessage }}
    </p>
  </section>
</template>
