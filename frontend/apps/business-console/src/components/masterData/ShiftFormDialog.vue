<script setup lang="ts">
/**
 * 班次的新建 / 编辑弹窗。排班与日历页和表单里的班次选择器（`DirectoryPicker creatable`）共用。
 *
 * 每次打开都是全新实例（调用方递增 `key`），所以表单只在 setup 里按 `editing` 初始化一次：
 * 不传是新建，传一行是编辑（拉详情回填时段与计薪，编码不可改）。
 */
import type {
  BusinessConsoleCreateShiftRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import type { DirectoryCreatedItem } from '@/components/business/directoryCreators'
import {
  useCreateMasterDataResource,
  useMasterDataResourceActions,
} from '@/composables/useBusinessMasterData'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  Spinner,
} from '@nerv-iip/ui'
import { computed, reactive, shallowRef } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

const props = defineProps<{
  /** 要编辑的班次行；不传即新建。 */
  editing?: BusinessConsoleResourceItem
}>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ created: [item: DirectoryCreatedItem] }>()

const context = useBusinessContextStore()
const creation = useCreateMasterDataResource<BusinessConsoleCreateShiftRequest>('shift')
const shiftActions = useMasterDataResourceActions('shift')

const editingCode = props.editing?.code ?? null
const showErrors = shallowRef(false)
const editLoading = shallowRef(false)
const form = reactive({ name: '', startsAt: '08:00', endsAt: '16:00', paidMinutes: '480' })

const paidValid = computed(() => (Number(form.paidMinutes) || 0) > 0)
const formValid = computed(() => isNonEmpty(form.name) && paidValid.value)
const crossesMidnight = computed(() => {
  const start = form.startsAt.trim()
  const end = form.endsAt.trim()
  return !!start && !!end && end <= start
})
const saving = computed(() => creation.pending.value || shiftActions.updatePending.value)

if (props.editing) void loadForEdit(props.editing)

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
// 班次起止后端以 HH:mm:ss 存；表单 <input type=time> 用 HH:mm，互转。
function toTimeInput(value?: string | null) {
  if (!value) return ''
  return value.slice(0, 5)
}
function toTimePayload(value: string) {
  const v = value.trim()
  if (!v) return undefined
  return v.length === 5 ? `${v}:00` : v
}
async function loadForEdit(row: BusinessConsoleResourceItem) {
  editLoading.value = true
  try {
    const d = await shiftActions.fetchDetail(row.code!)
    form.name = d?.name ?? row.displayName ?? ''
    form.startsAt = toTimeInput(d?.startsAt) || '08:00'
    form.endsAt = toTimeInput(d?.endsAt) || '16:00'
    form.paidMinutes = d?.paidMinutes != null ? String(d.paidMinutes) : '480'
  } finally {
    editLoading.value = false
  }
}
async function submit() {
  if (!formValid.value) {
    showErrors.value = true
    return
  }
  const name = form.name.trim()
  if (editingCode) {
    try {
      await shiftActions.update(editingCode, {
        name,
        startsAt: toTimePayload(form.startsAt),
        endsAt: toTimePayload(form.endsAt),
        paidMinutes: Number(form.paidMinutes) || 480,
      })
      notifySuccess(`班次「${name}」已更新。`)
      open.value = false
    } catch (error) {
      notifyOperationFailure('更新班次失败', error, '更新班次失败，请稍后重试。')
    }
    return
  }
  try {
    const response = await creation.create({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      name,
      startsAt: form.startsAt.trim() || undefined,
      endsAt: form.endsAt.trim() || undefined,
      paidMinutes: Number(form.paidMinutes) || 480,
    })
    notifySuccess(`班次「${name}」已创建。`)
    emit('created', { code: response.data!.code!, name })
    open.value = false
  } catch (error) {
    notifyOperationFailure('保存班次失败', error, '保存班次失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>{{ editingCode ? `编辑班次 · ${editingCode}` : '新建班次' }}</NvDialogTitle>
        <NvDialogDescription class="sr-only">{{
          editingCode ? `班次 ${editingCode}` : '新建班次'
        }}</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submit">
        <CarriedContextSummary
          v-if="editingCode"
          label="班次标识"
          :items="[{ label: '班次编码', value: editingCode }]"
        />
        <p v-if="showErrors && !formValid" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="showErrors && !isNonEmpty(form.name)">
            <NvFieldLabel for="shift-name"
              >班次名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="shift-name" v-model="form.name" autocomplete="off" required />
          </NvField>
          <NvField>
            <NvFieldLabel for="shift-start">开始时间</NvFieldLabel>
            <NvInput id="shift-start" v-model="form.startsAt" type="time" />
          </NvField>
          <NvField>
            <NvFieldLabel for="shift-end">结束时间</NvFieldLabel>
            <NvInput id="shift-end" v-model="form.endsAt" type="time" />
            <NvFieldDescription v-if="crossesMidnight"
              >结束早于开始，按跨天班次处理。</NvFieldDescription
            >
          </NvField>
          <NvField :data-invalid="showErrors && !paidValid">
            <NvFieldLabel for="shift-paid"
              >计薪时长（分钟） <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput
              id="shift-paid"
              v-model="form.paidMinutes"
              type="number"
              min="1"
              inputmode="numeric"
            />
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="saving || editLoading">
            <Spinner v-if="saving" aria-hidden="true" />{{ editingCode ? '保存修改' : '保存班次' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
