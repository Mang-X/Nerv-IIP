<script setup lang="ts">
import type { ConsoleIamUserResponse } from '@nerv-iip/api-client'
import { getConsoleIamUserEffectiveAccessQueryOptions } from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useQuery } from '@pinia/colada'
import {
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogHeader,
  NvDialogTitle,
} from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{ user: ConsoleIamUserResponse }>()
const open = defineModel<boolean>('open', { required: true })
const auth = useAuthStore()

const { data, error, isLoading } = useQuery(() => {
  const principal = auth.principal
  const orgId = principal?.organizationId ?? ''
  const envId = principal?.environmentId ?? ''
  const userId = props.user.userId ?? ''
  const options = getConsoleIamUserEffectiveAccessQueryOptions({
    path: { userId },
    query: { orgId, envId },
  })
  return {
    ...options,
    enabled: open.value && Boolean(userId && orgId && envId),
    key: [...options.key, principal?.principalId ?? ''],
  }
})

const access = computed(() => data.value?.data)
const roles = computed(
  () => new Map(access.value?.roles?.map((role) => [role.roleId, role.roleName])),
)
const accountStatus: Record<string, string> = {
  active: '有效',
  disabled: '已禁用',
  expired: '已过期',
  locked: '已锁定',
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="max-h-[85vh] min-w-0 overflow-y-auto sm:max-w-3xl">
      <NvDialogHeader>
        <NvDialogTitle>有效权限与来源</NvDialogTitle>
        <NvDialogDescription
          >{{ user.loginName }} · {{ auth.principal?.organizationId }} /
          {{ auth.principal?.environmentId }}</NvDialogDescription
        >
      </NvDialogHeader>
      <p v-if="isLoading" role="status">正在读取授权。</p>
      <p v-else-if="error || data?.success === false" role="alert" class="text-destructive">
        无法读取用户授权。
      </p>
      <template v-else-if="access">
        <p class="text-sm">
          账号{{ accountStatus[access.status ?? ''] }} · 授权版本 {{ access.permissionVersion }}
        </p>
        <dl class="grid gap-2 text-sm">
          <div>
            <dt class="font-medium">绑定角色</dt>
            <dd>
              {{
                access.roles?.map((role) => role.roleName).join('、') || '当前组织环境没有角色绑定'
              }}
            </dd>
          </div>
          <div>
            <dt class="font-medium">成员直接范围</dt>
            <dd>
              {{
                access.membershipDataScopes
                  ?.map((scope) => `${scope.scopeType} · ${scope.scopeCode}`)
                  .join('、') || '未配置'
              }}
            </dd>
          </div>
        </dl>
        <p v-if="!access.permissions?.length" class="text-sm text-muted-foreground">
          当前组织环境没有有效权限。
        </p>
        <details
          v-for="permission in access.permissions"
          :key="permission.permissionCode"
          class="min-w-0 rounded-md border p-3 text-sm"
        >
          <summary class="cursor-pointer break-all font-mono">
            {{ permission.permissionCode }}
          </summary>
          <p class="mt-2">
            授予角色：{{ permission.roleIds?.map((id) => roles.get(id) ?? id).join('、') }}
          </p>
          <ul v-if="permission.scopeGrants?.length" class="mt-2 grid gap-1">
            <li
              v-for="grant in permission.scopeGrants"
              :key="`${grant.sourceKind}:${grant.sourceId}:${grant.scopeKind}:${grant.scopeId}`"
              class="break-all"
            >
              {{ grant.sourceKind === 'role' ? roles.get(grant.sourceId ?? '') : '成员直接范围' }}
              · {{ grant.scopeKind }} · {{ grant.scopeId }}
            </li>
          </ul>
          <p v-else class="text-muted-foreground">无显式数据范围</p>
        </details>
      </template>
    </NvDialogContent>
  </NvDialog>
</template>
