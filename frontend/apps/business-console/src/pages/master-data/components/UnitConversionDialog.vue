<script setup lang="ts">
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
} from '@nerv-iip/ui'
import { ArrowRightLeftIcon } from '@lucide/vue'
import { inOptions, isPrecisionValid, isFactorValid, isOffsetValid } from '../unitFormValues'

defineProps<{
  conversionShowErrors: boolean
  canCreateConversion: boolean
  uomSelectOptions: { value: string; label: string }[]
  roundingOptions: { value: string; label: string }[]
  createUomConversionPending: boolean
}>()

const conversionOpen = defineModel<boolean>('conversionOpen', { required: true })

const fromUom = defineModel<string>('fromUom', { required: true })

const toUom = defineModel<string>('toUom', { required: true })

const factor = defineModel<string>('factor', { required: true })

const offset = defineModel<string>('offset', { required: true })

const roundingMode = defineModel<string>('roundingMode', { required: true })

const precision = defineModel<string>('precision', { required: true })

const emit = defineEmits<{
  prepare: []
  submit: []
}>()
</script>

<template>
  <NvDialog v-model:open="conversionOpen">
    <NvDialogTrigger as-child>
      <NvButton size="sm" type="button" @click="emit('prepare')">
        <ArrowRightLeftIcon aria-hidden="true" />
        新建换算关系
      </NvButton>
    </NvDialogTrigger>
    <NvDialogContent class="sm:max-w-2xl">
      <NvDialogHeader>
        <NvDialogTitle>新建换算关系</NvDialogTitle>
        <NvDialogDescription class="sr-only">新建单位换算关系</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="emit('submit')">
        <p
          v-if="conversionShowErrors && !canCreateConversion"
          class="text-sm text-destructive"
          role="alert"
        >
          请检查带 * 的必填项：源单位与目标单位不能相同，换算系数须大于 0（已标红）。
        </p>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField
            :data-invalid="
              conversionShowErrors && (!inOptions(uomSelectOptions, fromUom) || fromUom === toUom)
            "
          >
            <NvFieldLabel for="conv-from"
              >源单位 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="fromUom">
              <NvSelectTrigger id="conv-from"
                ><NvSelectValue placeholder="请选择源单位"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in uomSelectOptions" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField
            :data-invalid="
              conversionShowErrors && (!inOptions(uomSelectOptions, toUom) || fromUom === toUom)
            "
          >
            <NvFieldLabel for="conv-to"
              >目标单位 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="toUom">
              <NvSelectTrigger id="conv-to"
                ><NvSelectValue placeholder="请选择目标单位"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in uomSelectOptions" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField :data-invalid="conversionShowErrors && !isFactorValid(factor)">
            <NvFieldLabel for="conv-factor"
              >换算系数 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput
              id="conv-factor"
              v-model="factor"
              type="number"
              min="0"
              step="any"
              autocomplete="off"
              placeholder="如 1000"
            />
            <!-- 换算口径（非显而易见），保留一行。 -->
            <NvFieldDescription>1 源单位等于多少目标单位。</NvFieldDescription>
          </NvField>
          <NvField :data-invalid="conversionShowErrors && !isOffsetValid(offset)">
            <NvFieldLabel for="conv-offset">偏移量</NvFieldLabel>
            <NvInput
              id="conv-offset"
              v-model="offset"
              type="number"
              step="any"
              autocomplete="off"
              placeholder="可留空"
            />
            <!-- 换算口径（非显而易见），保留一行。 -->
            <NvFieldDescription>线性换算的常数项，如温标换算。</NvFieldDescription>
          </NvField>
          <NvField
            :data-invalid="conversionShowErrors && !inOptions(roundingOptions, roundingMode)"
          >
            <NvFieldLabel for="conv-rounding"
              >取整方式 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="roundingMode">
              <NvSelectTrigger id="conv-rounding"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in roundingOptions" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField :data-invalid="conversionShowErrors && !isPrecisionValid(precision)">
            <NvFieldLabel for="conv-precision">小数精度</NvFieldLabel>
            <NvInput
              id="conv-precision"
              v-model="precision"
              type="number"
              min="0"
              step="1"
              autocomplete="off"
              placeholder="可留空"
            />
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="conversionOpen = false">取消</NvButton>
          <NvButton type="submit" :disabled="createUomConversionPending">
            <NvSpinner v-if="createUomConversionPending" aria-hidden="true" />
            保存换算关系
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
