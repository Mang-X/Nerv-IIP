<script setup lang="ts">
import type { ConsoleIamRoleResponse, ConsoleIamUserResponse } from '@nerv-iip/api-client'
import UserRoleSelector from '@/components/iam/UserRoleSelector.vue'
import {
  Button,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Field,
  FieldDescription,
  FieldLabel,
} from '@nerv-iip/ui'
import { shallowRef, watch } from 'vue'

const props = defineProps<{
  currentRoleIds?: string[] | null
  disabled?: boolean
  roles: ConsoleIamRoleResponse[]
  user?: ConsoleIamUserResponse
}>()

const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  submit: [roleIds: string[]]
}>()

const selectedRoleIds = shallowRef<string[]>([])

watch(
  () => [open.value, props.currentRoleIds] as const,
  ([isOpen, current]) => {
    if (isOpen) {
      selectedRoleIds.value = [...(current ?? [])].sort()
    }
  },
  { immediate: true },
)

function handleSubmit() {
  emit('submit', selectedRoleIds.value)
  open.value = false
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent>
      <DialogHeader>
        <DialogTitle>分配角色</DialogTitle>
        <DialogDescription>
          为用户 {{ user?.loginName || user?.userId }} 设置在当前组织环境中的角色。
        </DialogDescription>
      </DialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <Field>
          <FieldLabel>角色</FieldLabel>
          <UserRoleSelector v-model="selectedRoleIds" id-prefix="iam-assign-role" :roles="roles" />
          <FieldDescription>
            {{
              selectedRoleIds.length === 0
                ? '未选择任何角色：保存后该用户将移出当前组织环境，无法再登录控制台。'
                : `已选 ${selectedRoleIds.length} 个角色。`
            }}
          </FieldDescription>
        </Field>

        <DialogFooter show-close-button>
          <Button type="submit" :disabled="disabled"> 保存 </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
