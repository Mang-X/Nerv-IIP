<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { MasterDataLifecyclePatch } from '@/composables/useBusinessMasterData'
import {
  NvAlertDialog,
  NvAlertDialogAction,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldDescription,
  NvFieldLabel,
  NvInput,
  NvRowActions,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { CircleSlashIcon, EyeIcon, PencilIcon, PlayIcon } from '@lucide/vue'
import { computed, ref, useId } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

export interface DetailField {
  label: string
  value: string
}

const props = defineProps<{
  /** 该行列表项（含 typed 字段）。 */
  row: BusinessConsoleResourceItem
  /** 业务名称，用于弹窗标题与提示。 */
  entityLabel: string
  /** 详情弹窗展示的字段（业务中文 label + 取自行的值）。 */
  detailFields: DetailField[]
  /**
   * 来自 useMasterDataResourceActions 的动作集合（停用/启用；编辑由页面自带表单处理）。
   * 补丁里的 `reason` 是**必填**：由确认框收集后随请求提交，后端写进生命周期审计。
   * 用 MasterDataLifecyclePatch 而不是自定义可选形状——否则组件这层又把必填约束松掉了。
   */
  actions: {
    disable: (code: string, patch: MasterDataLifecyclePatch) => Promise<unknown>
    enable: (code: string, patch: MasterDataLifecyclePatch) => Promise<unknown>
    disablePending: { value: boolean }
    enablePending: { value: boolean }
  }
}>()

// 「编辑」交由页面打开各自的全字段表单（带回填），故此处只发事件，不在组件内编辑。
const emit = defineEmits<{ edit: [row: BusinessConsoleResourceItem] }>()

const detailOpen = ref(false)
const toggleOpen = ref(false)
/**
 * 停用 / 重新启用的业务原因：必填，随请求提交并进生命周期审计（#878）。
 * 后端 `SetMasterDataResourceEnabledCommandHandler` 对空原因稳定拒绝，界面必须先拦住。
 */
const reason = ref('')
// 本组件按行渲染（每行一个实例），label ↔ input 的 id 必须逐实例唯一。
const reasonInputId = useId()
const reasonMaxLength = 500

const isActive = computed(() => props.row.active !== false)
const actionLabel = computed(() => (isActive.value ? '停用' : '启用'))
const togglePending = computed(
  () => props.actions.disablePending.value || props.actions.enablePending.value,
)
const canConfirmToggle = computed(() => reason.value.trim().length > 0 && !togglePending.value)

function openToggle() {
  // 每次打开都从空白开始：上一条原因不能被当成这一次的理由带进审计。
  reason.value = ''
  toggleOpen.value = true
}

async function confirmToggle() {
  const code = props.row.code
  const trimmedReason = reason.value.trim()
  if (!code || !trimmedReason) return
  const active = isActive.value
  try {
    if (active) {
      await props.actions.disable(code, { reason: trimmedReason })
      notifySuccess(`${props.entityLabel}已停用。`)
    } else {
      await props.actions.enable(code, { reason: trimmedReason })
      notifySuccess(`${props.entityLabel}已启用。`)
    }
    toggleOpen.value = false
    reason.value = ''
  } catch (error) {
    // 失败时保留已填原因：用户重试不必重新组织措辞。
    notifyOperationFailure(
      `${props.entityLabel}${active ? '停用' : '启用'}失败`,
      error,
      `${props.entityLabel}${active ? '停用' : '启用'}失败，请稍后重试。`,
    )
  }
}
</script>

<template>
  <NvRowActions :label="`${entityLabel}操作 ${row.code ?? ''}`">
    <NvDropdownMenuItem @click="detailOpen = true">
      <EyeIcon aria-hidden="true" />
      查看详情
    </NvDropdownMenuItem>
    <NvDropdownMenuItem :disabled="!row.code" @click="emit('edit', row)">
      <PencilIcon aria-hidden="true" />
      编辑
    </NvDropdownMenuItem>
    <NvDropdownMenuItem :disabled="!row.code" @click="openToggle">
      <CircleSlashIcon v-if="isActive" aria-hidden="true" />
      <PlayIcon v-else aria-hidden="true" />
      {{ actionLabel }}
    </NvDropdownMenuItem>
  </NvRowActions>

  <!-- 查看详情（只读） -->
  <NvDialog v-model:open="detailOpen">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>{{ entityLabel }}详情</NvDialogTitle>
        <NvDialogDescription class="sr-only">{{
          row.displayName ?? row.code ?? ''
        }}</NvDialogDescription>
      </NvDialogHeader>
      <dl class="grid gap-3 sm:grid-cols-2">
        <div v-for="field in detailFields" :key="field.label" class="grid gap-1">
          <dt class="text-xs text-muted-foreground">{{ field.label }}</dt>
          <dd class="text-sm">{{ field.value || '无' }}</dd>
        </div>
        <div class="grid gap-1">
          <dt class="text-xs text-muted-foreground">状态</dt>
          <dd><NvStatusBadge :value="row.active === false ? 'disabled' : 'active'" /></dd>
        </div>
      </dl>
      <NvDialogFooter>
        <NvButton type="button" variant="outline" @click="detailOpen = false">关闭</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>

  <!-- 停用 / 启用 二次确认 -->
  <NvAlertDialog v-model:open="toggleOpen">
    <NvAlertDialogContent>
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>
          {{ isActive ? `确认停用该${entityLabel}？` : `确认启用该${entityLabel}？` }}
        </NvAlertDialogTitle>
        <NvAlertDialogDescription>
          {{
            isActive
              ? '停用后将不能用于新建/计划，已有记录不受影响。'
              : '启用后可重新用于新建与计划。'
          }}
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <NvField>
        <NvFieldLabel :for="reasonInputId">
          {{ actionLabel }}原因 <span class="text-destructive">*</span>
        </NvFieldLabel>
        <NvInput
          :id="reasonInputId"
          v-model="reason"
          data-testid="lifecycle-reason"
          required
          :maxlength="reasonMaxLength"
          :placeholder="
            isActive ? '说明停用依据，如设备报废、供应商终止合作' : '说明重新启用依据，如整改完成'
          "
        />
        <NvFieldDescription>原因会记入生命周期审计，可按对象回溯。</NvFieldDescription>
      </NvField>
      <NvAlertDialogFooter>
        <NvAlertDialogCancel>取消</NvAlertDialogCancel>
        <NvAlertDialogAction
          :variant="isActive ? 'destructive' : 'default'"
          :disabled="!canConfirmToggle"
          @click="confirmToggle"
        >
          {{ isActive ? '确认停用' : '确认启用' }}
        </NvAlertDialogAction>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
