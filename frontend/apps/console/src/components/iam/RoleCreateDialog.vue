<script setup lang="ts">
import type { ConsoleIamPermissionResponse } from '@nerv-iip/api-client'
import RolePermissionEditor from '@/components/iam/RolePermissionEditor.vue'
import {
  Button,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  Input,
} from '@nerv-iip/ui'
import { reactive, shallowRef, watch } from 'vue'

const props = withDefaults(defineProps<{
  pending?: boolean
  permissions: ConsoleIamPermissionResponse[]
}>(), {
  pending: false,
})

const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  submit: [payload: { roleName: string, permissionCodes: string[] }]
}>()

const roleName = shallowRef('')
const permissionCodes = shallowRef<string[]>([])
const errors = reactive({
  roleName: '',
})

function clearErrors() {
  errors.roleName = ''
}

function resetForm() {
  roleName.value = ''
  permissionCodes.value = []
  clearErrors()
}

function validate() {
  clearErrors()
  errors.roleName = roleName.value.trim() ? '' : '请输入角色名称。'

  return !errors.roleName
}

function handleSubmit() {
  if (!validate()) {
    return
  }

  emit('submit', {
    permissionCodes: [...permissionCodes.value].sort(),
    roleName: roleName.value.trim(),
  })
}

watch(open, (isOpen) => {
  if (!isOpen) {
    resetForm()
  }
})
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent
      data-testid="role-create-dialog-content"
      class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl"
    >
      <DialogHeader>
        <DialogTitle>新建角色</DialogTitle>
        <DialogDescription>
          创建 IAM 角色并从权限目录中分配权限。
        </DialogDescription>
      </DialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <FieldGroup>
          <Field>
            <FieldLabel for="iam-create-role-name">角色名称</FieldLabel>
            <Input
              id="iam-create-role-name"
              v-model="roleName"
              :aria-invalid="Boolean(errors.roleName)"
              autocomplete="off"
            />
            <FieldError v-if="errors.roleName" :errors="[errors.roleName]" />
          </Field>

          <RolePermissionEditor
            v-model="permissionCodes"
            :permissions="props.permissions"
          />
        </FieldGroup>

        <DialogFooter show-close-button>
          <Button type="submit" :disabled="props.pending">
            新建角色
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
