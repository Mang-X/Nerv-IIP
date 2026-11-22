<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldError,
  NvFieldLabel,
  NvInput,
  NvRadioGroup,
  NvRadioGroupItem,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useAlarmPage } from '../useAlarmPage'

type View = ShallowUnwrapRef<ReturnType<typeof useAlarmPage>>

defineProps<{
  DURATION_PRESETS: View['DURATION_PRESETS']
  MAX_SHELVE_MINUTES: View['MAX_SHELVE_MINUTES']
  abandonShelve: View['abandonShelve']
  confirmShelve: View['confirmShelve']
  customMinutesError: View['customMinutesError']
  onShelveOpenChange: View['onShelveOpenChange']
  reasonError: View['reasonError']
  reasonRequired: View['reasonRequired']
  shelve: View['shelve']
  shelveContextItems: View['shelveContextItems']
  shelveInvalid: View['shelveInvalid']
  shelveIsBatch: View['shelveIsBatch']
}>()
</script>

<template>
  <NvDialog :open="shelve.open" @update:open="onShelveOpenChange">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>{{
          shelveIsBatch ? `批量搁置 ${shelve.targets.length} 条报警` : '搁置报警'
        }}</NvDialogTitle>
        <NvDialogDescription>
          <template v-if="shelve.locked"
            >{{ shelve.targets.length }} 条搁置失败，请原样重试或放弃重试。</template
          >
          <template v-else>搁置期间不再提醒，到期自动恢复。</template>
        </NvDialogDescription>
      </NvDialogHeader>

      <div class="grid gap-4 py-1">
        <!-- 单条搁置：报警事实由所选行带出，只读呈现。 -->
        <CarriedContextSummary v-if="!shelveIsBatch" label="搁置对象" :items="shelveContextItems" />
        <NvField>
          <NvFieldLabel>搁置时长</NvFieldLabel>
          <NvRadioGroup
            v-model="shelve.duration"
            :disabled="shelve.locked"
            class="grid grid-cols-2 gap-2.5"
          >
            <NvRadioGroupItem
              v-for="preset in DURATION_PRESETS"
              :key="preset.value"
              :value="preset.value"
              >{{ preset.label }}</NvRadioGroupItem
            >
          </NvRadioGroup>
        </NvField>

        <NvField v-if="shelve.duration === 'custom'">
          <NvFieldLabel for="shelve-custom-minutes"
            >自定义分钟数（1–{{ MAX_SHELVE_MINUTES }}）</NvFieldLabel
          >
          <NvInput
            id="shelve-custom-minutes"
            v-model="shelve.customMinutes"
            type="number"
            min="1"
            :max="MAX_SHELVE_MINUTES"
            step="1"
            class="w-40"
            placeholder="分钟"
            :disabled="shelve.locked"
            :invalid="Boolean(customMinutesError)"
            :aria-invalid="customMinutesError ? 'true' : undefined"
            :aria-describedby="customMinutesError ? 'shelve-custom-minutes-error' : undefined"
          />
          <NvFieldError
            v-if="customMinutesError"
            id="shelve-custom-minutes-error"
            :errors="[customMinutesError]"
          />
        </NvField>

        <NvField>
          <NvFieldLabel for="shelve-reason"
            >搁置原因{{ reasonRequired ? '（必填）' : '（可选）' }}</NvFieldLabel
          >
          <NvInput
            id="shelve-reason"
            v-model="shelve.reason"
            placeholder="例如：等待备件到货 / 计划内检修"
            :disabled="shelve.locked"
            :invalid="Boolean(reasonError)"
            :aria-invalid="reasonError ? 'true' : undefined"
            :aria-describedby="reasonError ? 'shelve-reason-error' : undefined"
          />
          <NvFieldError v-if="reasonError" id="shelve-reason-error" :errors="[reasonError]" />
        </NvField>
      </div>

      <NvDialogFooter>
        <NvButton
          v-if="shelve.locked"
          type="button"
          variant="outline"
          :disabled="shelve.submitting"
          @click="abandonShelve"
          >放弃重试</NvButton
        >
        <NvButton v-else type="button" variant="outline" @click="onShelveOpenChange(false)"
          >取消</NvButton
        >
        <NvButton
          type="button"
          :disabled="shelve.submitting || shelveInvalid"
          @click="confirmShelve"
        >
          {{
            shelve.locked
              ? `重试 ${shelve.targets.length} 条`
              : shelveIsBatch
                ? `搁置 ${shelve.targets.length} 条`
                : '确认搁置'
          }}
        </NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
