<script setup lang="ts">
import type { ConsoleCreateIamUserRequest, ConsoleIamRoleResponse } from '@nerv-iip/api-client'
import UserRoleSelector from '@/components/iam/UserRoleSelector.vue'
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
  Input,
} from '@nerv-iip/ui'
import { reactive, watch } from 'vue'

defineProps<{
  roles: ConsoleIamRoleResponse[]
}>()

const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  submit: [payload: { roleIds: string[]; user: ConsoleCreateIamUserRequest }]
}>()

const form = reactive({
  accountExpiresDate: '',
  email: '',
  loginName: '',
  password: '',
  roleIds: [] as string[],
})
const errors = reactive({
  email: '',
  loginName: '',
  password: '',
  roleIds: '',
})

function resetForm() {
  form.accountExpiresDate = ''
  form.email = ''
  form.loginName = ''
  form.password = ''
  form.roleIds = []
  clearErrors()
}

function clearErrors() {
  errors.email = ''
  errors.loginName = ''
  errors.password = ''
  errors.roleIds = ''
}

function validate() {
  clearErrors()
  errors.loginName = form.loginName.trim() ? '' : '请输入登录名。'
  errors.email = form.email.trim() ? '' : '请输入邮箱。'
  errors.password = form.password ? '' : '请输入密码。'
  errors.roleIds = form.roleIds.length > 0 ? '' : '请至少选择一个角色。'

  return !errors.loginName && !errors.email && !errors.password && !errors.roleIds
}

function handleSubmit() {
  if (!validate()) {
    return
  }

  emit('submit', {
    roleIds: form.roleIds,
    user: {
      accountExpiresAtUtc: toUtcEndOfDay(form.accountExpiresDate),
      email: form.email.trim(),
      loginName: form.loginName.trim(),
      password: form.password,
    },
  })
  open.value = false
  resetForm()
}

watch(open, (isOpen) => {
  if (!isOpen) {
    resetForm()
  }
})

function toUtcEndOfDay(value: string) {
  return value ? `${value}T23:59:59Z` : undefined
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>新建用户</NvDialogTitle>
        <NvDialogDescription>
          创建一个控制台用户并加入当前组织环境，填写登录名、邮箱、初始密码与角色。
        </NvDialogDescription>
      </NvDialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <FieldGroup>
          <Field>
            <FieldLabel for="iam-create-login-name">登录名</FieldLabel>
            <Input
              id="iam-create-login-name"
              v-model="form.loginName"
              :aria-invalid="Boolean(errors.loginName)"
              autocomplete="username"
            />
            <FieldError v-if="errors.loginName" :errors="[errors.loginName]" />
          </Field>

          <Field>
            <FieldLabel for="iam-create-email">邮箱</FieldLabel>
            <Input
              id="iam-create-email"
              v-model="form.email"
              :aria-invalid="Boolean(errors.email)"
              autocomplete="email"
              type="email"
            />
            <FieldError v-if="errors.email" :errors="[errors.email]" />
          </Field>

          <Field>
            <FieldLabel for="iam-create-password">密码</FieldLabel>
            <Input
              id="iam-create-password"
              v-model="form.password"
              :aria-invalid="Boolean(errors.password)"
              autocomplete="new-password"
              type="password"
            />
            <FieldError v-if="errors.password" :errors="[errors.password]" />
          </Field>

          <Field>
            <FieldLabel for="iam-create-account-expires">账号有效期</FieldLabel>
            <Input id="iam-create-account-expires" v-model="form.accountExpiresDate" type="date" />
          </Field>

          <Field>
            <FieldLabel>角色</FieldLabel>
            <UserRoleSelector
              v-model="form.roleIds"
              id-prefix="iam-create-role"
              :invalid="Boolean(errors.roleIds)"
              :roles="roles"
            />
            <FieldError v-if="errors.roleIds" :errors="[errors.roleIds]" />
          </Field>
        </FieldGroup>

        <NvDialogFooter show-close-button>
          <Button type="submit"> 新建用户 </Button>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
