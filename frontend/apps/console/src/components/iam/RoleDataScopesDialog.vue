<script setup lang="ts">
import type { ConsoleIamDataScope, ConsoleIamRoleResponse } from '@nerv-iip/api-client'
import { patchConsoleIamRoleDataScopesMutationOptions } from '@nerv-iip/api-client'
import { toConsoleIamError } from '@/api/iam'
import { useMutation } from '@pinia/colada'
import {
  Button,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogHeader,
  NvDialogTitle,
  NvInput,
} from '@nerv-iip/ui'
import { ref } from 'vue'

const props = defineProps<{ role: ConsoleIamRoleResponse }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ saved: []; conflict: [] }>()
const scopes = ref<ConsoleIamDataScope[]>(
  (props.role.dataScopes ?? []).map((scope) => ({ ...scope })),
)
const error = ref<string>()
const { mutateAsync, isLoading } = useMutation(patchConsoleIamRoleDataScopesMutationOptions())

const scopeTypes = [
  'organization',
  'site',
  'work-center',
  'self',
  'team',
  'workshop',
  'production-line',
]

async function save() {
  const roleId = props.role.roleId
  const expectedVersion = props.role.scopeVersion
  if (!roleId || expectedVersion === undefined || isLoading.value) return
  const dataScopes = scopes.value.map((scope) => ({
    scopeType: scope.scopeType,
    scopeCode: scope.scopeType === 'self' ? 'self' : scope.scopeCode?.trim(),
  }))
  if (dataScopes.some((scope) => !scope.scopeCode)) {
    error.value = '请填写每条范围的标识。'
    return
  }
  if (
    !dataScopes.length &&
    props.role.dataScopes?.length &&
    !window.confirm('确认清空此角色的全部数据范围？')
  )
    return

  error.value = undefined
  try {
    const result = await mutateAsync({ path: { roleId }, body: { expectedVersion, dataScopes } })
    if (!result.success) throw result
    emit('saved')
    open.value = false
  } catch (failure) {
    const reason = toConsoleIamError(failure)
    if (reason.status === 409) {
      emit('conflict')
    } else {
      error.value = reason.message
    }
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
      <NvDialogHeader>
        <NvDialogTitle>编辑角色数据范围</NvDialogTitle>
        <NvDialogDescription
          >{{ role.roleName }} · 范围仅适用于此角色授予的权限。</NvDialogDescription
        >
      </NvDialogHeader>
      <form class="grid gap-3" @submit.prevent="save">
        <div
          v-for="(scope, index) in scopes"
          :key="index"
          class="grid grid-cols-[8rem_minmax(0,1fr)_auto] gap-2"
        >
          <select
            v-model="scope.scopeType"
            class="rounded-md border bg-background px-2 text-sm"
            :aria-label="`范围类型 ${index + 1}`"
          >
            <option v-for="type in scopeTypes" :key="type" :value="type">{{ type }}</option>
          </select>
          <NvInput
            v-if="scope.scopeType !== 'self'"
            v-model="scope.scopeCode"
            :aria-label="`范围标识 ${index + 1}`"
            placeholder="范围标识"
            required
          />
          <span v-else class="self-center text-sm text-muted-foreground">解析为当前用户</span>
          <Button type="button" variant="outline" @click="scopes.splice(index, 1)">删除</Button>
        </div>
        <p v-if="!scopes.length" class="text-sm text-muted-foreground">未配置角色数据范围。</p>
        <p v-if="error" role="alert" class="text-sm text-destructive">{{ error }}</p>
        <div class="flex justify-between gap-2">
          <Button
            type="button"
            variant="outline"
            @click="scopes.push({ scopeType: 'organization', scopeCode: '' })"
            >添加范围</Button
          >
          <Button type="submit" :disabled="isLoading">保存范围</Button>
        </div>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
