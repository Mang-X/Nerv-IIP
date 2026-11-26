<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import {
  NvButton,
  NvCheckbox,
  NvDatePicker,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldDescription,
  NvFieldError,
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
import { PlusIcon, Trash2Icon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useCodeRulesPage } from '../useCodeRulesPage'

type View = ShallowUnwrapRef<ReturnType<typeof useCodeRulesPage>>

defineProps<{
  CHANGE_REASON_MAX_LENGTH: View['CHANGE_REASON_MAX_LENGTH']
  CHECKSUM_ALGORITHM_OPTIONS: View['CHECKSUM_ALGORITHM_OPTIONS']
  DATE_FORMAT_OPTIONS: View['DATE_FORMAT_OPTIONS']
  RESET_OPTIONS: View['RESET_OPTIONS']
  SCOPE_OPTIONS: View['SCOPE_OPTIONS']
  SEGMENT_TYPE_OPTIONS: View['SEGMENT_TYPE_OPTIONS']
  TRANSFORM_OPTIONS: View['TRANSFORM_OPTIONS']
  addSegment: View['addSegment']
  canSubmit: View['canSubmit']
  changeReasonError: View['changeReasonError']
  changeReasonValid: View['changeReasonValid']
  createPending: View['createPending']
  displayNameValid: View['displayNameValid']
  editingCurrentVersion: View['editingCurrentVersion']
  editingRuleKey: View['editingRuleKey']
  form: View['form']
  formOpen: View['formOpen']
  formPreview: View['formPreview']
  formPreviewPending: View['formPreviewPending']
  onFormOpenChange: View['onFormOpenChange']
  previewFormSample: View['previewFormSample']
  removeSegment: View['removeSegment']
  segmentErrors: View['segmentErrors']
  sequenceCountError: View['sequenceCountError']
  showErrors: View['showErrors']
  submitForm: View['submitForm']
}>()
</script>

<template>
  <NvDialog :open="formOpen" @update:open="onFormOpenChange">
    <NvDialogContent class="sm:max-w-3xl">
      <NvDialogHeader>
        <NvDialogTitle>新建编码规则版本</NvDialogTitle>
        <!-- 规则键与当前版本已在下方只读区呈现；此处仅供读屏播报。 -->
        <NvDialogDescription class="sr-only">
          规则 {{ editingRuleKey }}，当前版本 {{ editingCurrentVersion || '无' }}
        </NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-5" novalidate @submit.prevent="submitForm">
        <CarriedContextSummary
          label="规则上下文"
          :items="[
            { label: '规则键', value: editingRuleKey },
            { label: '当前版本', value: editingCurrentVersion },
          ]"
        />
        <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
          请填写带 * 的必填项（名称、变更原因、创建人），并修正下方红字提示。
        </p>

        <FormSectionTitle>版本信息</FormSectionTitle>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="showErrors && !displayNameValid">
            <NvFieldLabel for="cr-name">名称 <span class="text-destructive">*</span></NvFieldLabel>
            <NvInput id="cr-name" v-model="form.displayName" placeholder="规则名称" />
          </NvField>
          <NvField>
            <NvFieldLabel for="cr-applies">适用对象</NvFieldLabel>
            <NvInput id="cr-applies" v-model="form.appliesTo" placeholder="如 物料 / 工单" />
          </NvField>
          <NvField>
            <NvFieldLabel for="cr-scope">范围</NvFieldLabel>
            <NvSelect v-model="form.scope">
              <NvSelectTrigger id="cr-scope"
                ><NvSelectValue placeholder="选择范围"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in SCOPE_OPTIONS" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel>生效时间</NvFieldLabel>
            <NvDatePicker
              v-model="form.effectiveFromUtc"
              placeholder="留空即时生效"
              class="w-full"
            />
          </NvField>
          <NvField :data-invalid="showErrors && !changeReasonValid">
            <NvFieldLabel for="cr-reason">
              变更原因 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvInput
              id="cr-reason"
              v-model="form.changeReason"
              required
              :maxlength="CHANGE_REASON_MAX_LENGTH"
              placeholder="说明本次规则调整的业务依据"
              :invalid="showErrors && !changeReasonValid"
              :aria-invalid="showErrors && !changeReasonValid ? 'true' : undefined"
              :aria-describedby="
                showErrors && changeReasonError
                  ? 'cr-reason-error cr-reason-count'
                  : 'cr-reason-help cr-reason-count'
              "
            />
            <div class="flex items-start justify-between gap-3">
              <NvFieldError
                v-if="showErrors && changeReasonError"
                id="cr-reason-error"
                :errors="[changeReasonError]"
              />
              <NvFieldDescription v-else id="cr-reason-help">
                原因会记入版本历史，供后续审计与追溯。
              </NvFieldDescription>
              <span
                id="cr-reason-count"
                class="ml-auto shrink-0 text-xs text-muted-foreground"
                aria-live="polite"
              >
                {{ form.changeReason.length }} / {{ CHANGE_REASON_MAX_LENGTH }}
              </span>
            </div>
          </NvField>
        </NvFieldGroup>

        <div class="flex items-center justify-between">
          <FormSectionTitle>段定义</FormSectionTitle>
          <NvButton type="button" variant="outline" size="sm" @click="addSegment">
            <PlusIcon aria-hidden="true" />
            增加段
          </NvButton>
        </div>

        <p v-if="showErrors && sequenceCountError" class="text-sm text-destructive" role="alert">
          {{ sequenceCountError }}
        </p>

        <div class="grid gap-2">
          <div
            v-for="(segment, index) in form.segments"
            :key="index"
            class="grid gap-2 rounded-md border p-3"
          >
            <div class="grid grid-cols-[8rem_1fr_auto] items-end gap-2">
              <NvField :data-invalid="showErrors && segment.type === ''">
                <NvFieldLabel :for="`cr-type-${index}`"
                  >类型 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvSelect v-model="segment.type">
                  <NvSelectTrigger :id="`cr-type-${index}`"
                    ><NvSelectValue placeholder="选择类型"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem
                      v-for="o in SEGMENT_TYPE_OPTIONS"
                      :key="o.value"
                      :value="o.value"
                      >{{ o.label }}</NvSelectItem
                    >
                  </NvSelectContent>
                </NvSelect>
              </NvField>

              <!-- 常量 -->
              <NvField
                v-if="segment.type === 'constant'"
                :data-invalid="showErrors && !!segmentErrors[index]"
              >
                <NvFieldLabel :for="`cr-value-${index}`"
                  >常量值 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvInput :id="`cr-value-${index}`" v-model="segment.value" placeholder="如 SKU" />
              </NvField>

              <!-- 日期 -->
              <NvField
                v-else-if="segment.type === 'date'"
                :data-invalid="showErrors && !!segmentErrors[index]"
              >
                <NvFieldLabel :for="`cr-format-${index}`"
                  >日期格式 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvSelect v-model="segment.format">
                  <NvSelectTrigger :id="`cr-format-${index}`"
                    ><NvSelectValue placeholder="选择日期格式"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem v-for="f in DATE_FORMAT_OPTIONS" :key="f" :value="f">{{
                      f
                    }}</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>

              <!-- 流水号 -->
              <div
                v-else-if="segment.type === 'sequence'"
                class="grid grid-cols-2 gap-2 sm:grid-cols-4"
              >
                <NvField :data-invalid="showErrors && !!segmentErrors[index]">
                  <NvFieldLabel :for="`cr-width-${index}`"
                    >宽度 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    :id="`cr-width-${index}`"
                    v-model="segment.width"
                    type="number"
                    min="1"
                    placeholder="4"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !!segmentErrors[index]">
                  <NvFieldLabel :for="`cr-start-${index}`"
                    >起始 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    :id="`cr-start-${index}`"
                    v-model="segment.start"
                    type="number"
                    min="1"
                    placeholder="1"
                  />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`cr-pad-${index}`">补位字符</NvFieldLabel>
                  <NvInput :id="`cr-pad-${index}`" v-model="segment.padChar" placeholder="0" />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`cr-reset-${index}`">重置</NvFieldLabel>
                  <NvSelect v-model="segment.reset">
                    <NvSelectTrigger :id="`cr-reset-${index}`"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem v-for="o in RESET_OPTIONS" :key="o.value" :value="o.value">{{
                        o.label
                      }}</NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
              </div>

              <!-- 字段 -->
              <div
                v-else-if="segment.type === 'field'"
                class="grid grid-cols-1 gap-2 sm:grid-cols-3"
              >
                <NvField :data-invalid="showErrors && !!segmentErrors[index]">
                  <NvFieldLabel :for="`cr-source-${index}`"
                    >源 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    :id="`cr-source-${index}`"
                    v-model="segment.source"
                    placeholder="如 categoryCode"
                  />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`cr-transform-${index}`">变换</NvFieldLabel>
                  <NvSelect v-model="segment.transform">
                    <NvSelectTrigger :id="`cr-transform-${index}`"
                      ><NvSelectValue
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="o in TRANSFORM_OPTIONS"
                        :key="o.value"
                        :value="o.value"
                        >{{ o.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="showErrors && !!segmentErrors[index]">
                  <NvFieldLabel :for="`cr-maxlen-${index}`"
                    >上限 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    :id="`cr-maxlen-${index}`"
                    v-model="segment.maxLength"
                    type="number"
                    min="1"
                    placeholder="如 8"
                  />
                </NvField>
              </div>

              <!-- 校验位 -->
              <NvField
                v-else-if="segment.type === 'checksum'"
                :data-invalid="showErrors && !!segmentErrors[index]"
              >
                <NvFieldLabel :for="`cr-algo-${index}`"
                  >算法 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvSelect v-model="segment.algorithm">
                  <NvSelectTrigger :id="`cr-algo-${index}`"
                    ><NvSelectValue placeholder="选择校验算法"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem v-for="a in CHECKSUM_ALGORITHM_OPTIONS" :key="a" :value="a">{{
                      a
                    }}</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>

              <div v-else />

              <NvButton
                type="button"
                variant="ghost"
                size="icon"
                aria-label="删除该段"
                :disabled="form.segments.length <= 1"
                @click="removeSegment(index)"
              >
                <Trash2Icon aria-hidden="true" />
              </NvButton>
            </div>

            <div class="flex items-center justify-between gap-3">
              <label
                :for="`cr-required-${index}`"
                class="flex cursor-pointer select-none items-center gap-2 text-sm"
              >
                <NvCheckbox :id="`cr-required-${index}`" v-model="segment.required" />
                <span>必填</span>
              </label>
              <p
                v-if="showErrors && segmentErrors[index]"
                class="text-sm text-destructive"
                role="alert"
              >
                {{ segmentErrors[index] }}
              </p>
            </div>
          </div>
        </div>

        <div class="flex items-center gap-2">
          <NvButton
            type="button"
            variant="outline"
            size="sm"
            :disabled="formPreviewPending"
            @click="previewFormSample"
          >
            <NvSpinner v-if="formPreviewPending" aria-hidden="true" />
            预览
          </NvButton>
          <code v-if="formPreview" class="rounded bg-muted px-2 py-1 text-sm font-mono">{{
            formPreview
          }}</code>
        </div>

        <NvDialogFooter>
          <NvButton
            type="button"
            variant="outline"
            :disabled="createPending"
            @click="onFormOpenChange(false)"
            >取消</NvButton
          >
          <NvButton type="submit" :disabled="createPending">
            <NvSpinner v-if="createPending" aria-hidden="true" />
            发布版本
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
