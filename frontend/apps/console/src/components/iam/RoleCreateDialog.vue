<script setup lang="ts">
import type { ConsoleIamPermissionResponse } from '@nerv-iip/api-client'
import RolePermissionEditor from '@/components/iam/RolePermissionEditor.vue'
import {
  Button,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  NvInput,
} from '@nerv-iip/ui'
import { reactive, shallowRef, watch } from 'vue'

const props = withDefaults(
  defineProps<{
    pending?: boolean
    permissions: ConsoleIamPermissionResponse[]
  }>(),
  {
    pending: false,
  },
)

const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  submit: [payload: { roleName: string; permissionCodes: string[] }]
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
  <NvDialog v-model:open="open">
    <NvDialogContent class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl">
      <NvDialogHeader>
        <NvDialogTitle>新建角色</NvDialogTitle>
        <NvDialogDescription> 创建 IAM 角色并从权限目录中分配权限。 </NvDialogDescription>
      </NvDialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <FieldGroup>
          <Field>
            <FieldLabel for="iam-create-role-name">角色名称</FieldLabel>
            <NvInput
              id="iam-create-role-name"
              v-model="roleName"
              :aria-invalid="Boolean(errors.roleName)"
              autocomplete="off"
            />
            <FieldError v-if="errors.roleName" :errors="[errors.roleName]" />
          </Field>

          <RolePermissionEditor v-model="permissionCodes" :permissions="props.permissions" />
        </FieldGroup>

        <NvDialogFooter show-close-button>
          <Button type="submit" :disabled="props.pending"> 新建角色 </Button>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
