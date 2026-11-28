<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useFacilitiesPage } from '../useFacilitiesPage'

type View = ShallowUnwrapRef<ReturnType<typeof useFacilitiesPage>>

defineProps<{
  NODE_LABEL: View['NODE_LABEL']
  NONE_OPTION: View['NONE_OPTION']
  canEdit: View['canEdit']
  editCode: View['editCode']
  editForm: View['editForm']
  editCalendars: View['editCalendars']
  editLoading: View['editLoading']
  editPending: View['editPending']
  editShowErrors: View['editShowErrors']
  editTimeZoneOptions: View['editTimeZoneOptions']
  editTitle: View['editTitle']
  editType: View['editType']
  isNonEmpty: View['isNonEmpty']
  sites: View['sites']
  submitEdit: View['submitEdit']
}>()

const editOpen = defineModel<View['editOpen']>('editOpen', { required: true })
</script>

<template>
  <NvDialog v-model:open="editOpen">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>{{ editTitle }}</NvDialogTitle>
        <NvDialogDescription class="sr-only"
          >{{ NODE_LABEL[editType] }} {{ editCode }}</NvDialogDescription
        >
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitEdit">
        <CarriedContextSummary
          :label="`${NODE_LABEL[editType]}标识`"
          :items="[{ label: `${NODE_LABEL[editType]}编码`, value: editCode }]"
        />
        <p v-if="editShowErrors && !canEdit" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <FormSectionTitle>基础信息</FormSectionTitle>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.name)">
            <NvFieldLabel for="edit-name"
              >{{ NODE_LABEL[editType] }}名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="edit-name" v-model="editForm.name" autocomplete="off" required />
          </NvField>
          <NvField v-if="editType === 'site'">
            <NvFieldLabel for="edit-tz">时区</NvFieldLabel>
            <NvSearchSelect
              id="edit-tz"
              v-model="editForm.timezone"
              :options="editTimeZoneOptions"
              placeholder="选择时区"
              aria-label="时区"
            />
          </NvField>
        </NvFieldGroup>

        <!-- 归属（可改挂上级；选项取页内已加载列表，跨层不成环） -->
        <template v-if="editType !== 'site'">
          <FormSectionTitle>归属</FormSectionTitle>
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField
              v-if="editType === 'workshop'"
              :data-invalid="editShowErrors && !isNonEmpty(editForm.siteCode)"
            >
              <NvFieldLabel for="edit-site"
                >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvSelect v-model="editForm.siteCode">
                <NvSelectTrigger id="edit-site"
                  ><NvSelectValue placeholder="请选择工厂"
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="s in sites.items.value" :key="s.code" :value="s.code ?? ''">
                    {{ s.displayName ?? s.code }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <template v-if="editType === 'production-line'">
              <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.siteCode)">
                <NvFieldLabel for="edit-line-site"
                  >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvSelect v-model="editForm.siteCode">
                  <NvSelectTrigger id="edit-line-site"
                    ><NvSelectValue placeholder="请选择工厂"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem
                      v-for="s in sites.items.value"
                      :key="s.code"
                      :value="s.code ?? NONE_OPTION"
                    >
                      {{ s.displayName ?? s.code }}
                    </NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="edit-line-workshop">所属车间</NvFieldLabel>
                <DirectoryPicker
                  id="edit-line-workshop"
                  v-model="editForm.workshopCode"
                  directory-type="workshop"
                  creatable
                  :parent="{ siteCode: editForm.siteCode }"
                  :create-context="{ siteCode: editForm.siteCode }"
                  placeholder="无（直挂工厂）"
                  clearable
                />
              </NvField>
            </template>
            <template v-if="editType === 'station'">
              <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.lineCode)">
                <NvFieldLabel for="edit-station-line"
                  >所属产线 <span class="text-destructive">*</span></NvFieldLabel
                >
                <DirectoryPicker
                  id="edit-station-line"
                  v-model="editForm.lineCode"
                  directory-type="production-line"
                  creatable
                  placeholder="请选择产线"
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="edit-station-wc">关联工作中心</NvFieldLabel>
                <DirectoryPicker
                  id="edit-station-wc"
                  v-model="editForm.workCenterCode"
                  directory-type="work-center"
                  :parent="{ lineCode: editForm.lineCode }"
                  placeholder="无"
                  clearable
                />
              </NvField>
            </template>
            <template v-if="editType === 'work-center'">
              <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.plantCode)">
                <NvFieldLabel for="edit-wc-plant"
                  >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvSelect v-model="editForm.plantCode">
                  <NvSelectTrigger id="edit-wc-plant"
                    ><NvSelectValue placeholder="请选择工厂"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem
                      v-for="s in sites.items.value"
                      :key="s.code"
                      :value="s.code ?? NONE_OPTION"
                    >
                      {{ s.displayName ?? s.code }}
                    </NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.lineCode)">
                <NvFieldLabel for="edit-wc-line"
                  >所属产线 <span class="text-destructive">*</span></NvFieldLabel
                >
                <DirectoryPicker
                  id="edit-wc-line"
                  v-model="editForm.lineCode"
                  directory-type="production-line"
                  creatable
                  :parent="{ siteCode: editForm.plantCode }"
                  :create-context="{ siteCode: editForm.plantCode }"
                  placeholder="请选择产线"
                />
              </NvField>
              <NvField :data-invalid="editShowErrors && !editForm.defaultCalendarCode">
                <NvFieldLabel for="edit-wc-calendar">默认工作日历</NvFieldLabel>
                <NvSelect v-model="editForm.defaultCalendarCode">
                  <NvSelectTrigger id="edit-wc-calendar"
                    ><NvSelectValue placeholder="请选择工作日历"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem
                      v-for="calendar in editCalendars"
                      :key="calendar.code"
                      :value="calendar.code ?? ''"
                    >
                      {{ calendar.displayName }}
                    </NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="edit-wc-capacity">日产能（分钟）</NvFieldLabel>
                <NvInput
                  id="edit-wc-capacity"
                  v-model="editForm.capacityMinutesPerDay"
                  type="number"
                  min="1"
                  step="1"
                  required
                />
              </NvField>
            </template>
          </NvFieldGroup>
        </template>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="editOpen = false">取消</NvButton>
          <NvButton type="submit" :disabled="editPending || editLoading">
            <NvSpinner v-if="editPending" aria-hidden="true" />
            保存修改
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
