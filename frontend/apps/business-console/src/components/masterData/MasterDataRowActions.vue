<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  Button,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DropdownMenuItem,
  Field,
  FieldDescription,
  FieldLabel,
  Input,
  RowActions,
  Spinner,
  StatusBadge,
  toast,
} from '@nerv-iip/ui'
import { CircleSlashIcon, EyeIcon, PencilIcon, PlayIcon } from 'lucide-vue-next'
import { ref, watch } from 'vue'

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
  /** 来自 useMasterDataResourceActions 的动作集合。 */
  actions: {
    update: (code: string, patch: { name: string }) => Promise<unknown>
    disable: (code: string) => Promise<unknown>
    enable: (code: string) => Promise<unknown>
    updatePending: { value: boolean }
    disablePending: { value: boolean }
    enablePending: { value: boolean }
  }
}>()

const detailOpen = ref(false)
const editOpen = ref(false)
const toggleOpen = ref(false)
const editName = ref('')

watch(editOpen, (open) => {
  if (open) editName.value = props.row.displayName ?? ''
})

function formatError(error: unknown) {
  return error instanceof Error ? error.message : '请求失败，请稍后重试。'
}

async function submitName() {
  const code = props.row.code
  const name = editName.value.trim()
  if (!code || !name) return
  try {
    await props.actions.update(code, { name })
    toast.success('已更新')
    editOpen.value = false
  }
  catch (error) {
    toast.error(formatError(error))
  }
}

async function confirmToggle() {
  const code = props.row.code
  if (!code) return
  const isActive = props.row.active !== false
  try {
    if (isActive) {
      await props.actions.disable(code)
      toast.success('已停用')
    }
    else {
      await props.actions.enable(code)
      toast.success('已启用')
    }
    toggleOpen.value = false
  }
  catch (error) {
    toast.error(formatError(error))
  }
}
</script>

<template>
  <RowActions :label="`${entityLabel}操作 ${row.code ?? ''}`">
    <DropdownMenuItem @click="detailOpen = true">
      <EyeIcon aria-hidden="true" />
      查看详情
    </DropdownMenuItem>
    <DropdownMenuItem :disabled="!row.code" @click="editOpen = true">
      <PencilIcon aria-hidden="true" />
      编辑名称
    </DropdownMenuItem>
    <DropdownMenuItem :disabled="!row.code" @click="toggleOpen = true">
      <CircleSlashIcon v-if="row.active !== false" aria-hidden="true" />
      <PlayIcon v-else aria-hidden="true" />
      {{ row.active !== false ? '停用' : '启用' }}
    </DropdownMenuItem>
  </RowActions>

  <!-- 查看详情（只读） -->
  <Dialog v-model:open="detailOpen">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>{{ entityLabel }}详情</DialogTitle>
        <DialogDescription>{{ row.displayName ?? row.code ?? '' }} 的关键信息。</DialogDescription>
      </DialogHeader>
      <dl class="grid gap-3 sm:grid-cols-2">
        <div v-for="field in detailFields" :key="field.label" class="grid gap-1">
          <dt class="text-xs text-muted-foreground">{{ field.label }}</dt>
          <dd class="text-sm">{{ field.value || '无' }}</dd>
        </div>
        <div class="grid gap-1">
          <dt class="text-xs text-muted-foreground">状态</dt>
          <dd><StatusBadge :value="row.active === false ? 'disabled' : 'active'" /></dd>
        </div>
      </dl>
      <DialogFooter>
        <Button type="button" variant="outline" @click="detailOpen = false">关闭</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>

  <!-- 编辑名称 -->
  <Dialog v-model:open="editOpen">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>编辑{{ entityLabel }}名称</DialogTitle>
        <DialogDescription>当前支持修改名称，其它字段编辑后续开放。</DialogDescription>
      </DialogHeader>
      <form class="grid gap-4" @submit.prevent="submitName">
        <Field>
          <FieldLabel for="edit-name">名称</FieldLabel>
          <Input id="edit-name" v-model="editName" autocomplete="off" required />
          <FieldDescription>编码不可修改。</FieldDescription>
        </Field>
        <DialogFooter>
          <Button type="button" variant="outline" @click="editOpen = false">取消</Button>
          <Button type="submit" :disabled="actions.updatePending.value || !editName.trim()">
            <Spinner v-if="actions.updatePending.value" aria-hidden="true" />
            保存
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>

  <!-- 停用 / 启用 二次确认 -->
  <AlertDialog v-model:open="toggleOpen">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>
          {{ row.active !== false ? `确认停用该${entityLabel}？` : `确认启用该${entityLabel}？` }}
        </AlertDialogTitle>
        <AlertDialogDescription>
          {{
            row.active !== false
              ? '停用后将不能用于新建/计划，已有记录不受影响。'
              : '启用后可重新用于新建与计划。'
          }}
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>取消</AlertDialogCancel>
        <AlertDialogAction
          :disabled="actions.disablePending.value || actions.enablePending.value"
          @click="confirmToggle"
        >
          {{ row.active !== false ? '确认停用' : '确认启用' }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
