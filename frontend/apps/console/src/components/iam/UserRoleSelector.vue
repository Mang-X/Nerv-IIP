<script setup lang="ts">
import type { ConsoleIamRoleResponse } from '@nerv-iip/api-client'
import { NvCheckbox } from '@nerv-iip/ui'

defineProps<{
  idPrefix: string
  invalid?: boolean
  roles: ConsoleIamRoleResponse[]
}>()

const selectedRoleIds = defineModel<string[]>({ default: () => [] })

function setSelected(roleId: string, checked: boolean | 'indeterminate') {
  const next = new Set(selectedRoleIds.value)
  if (checked === true) {
    next.add(roleId)
  } else {
    next.delete(roleId)
  }

  selectedRoleIds.value = [...next].sort()
}
</script>

<template>
  <div
    role="group"
    :aria-invalid="invalid"
    class="grid max-h-[min(40vh,20rem)] gap-1 overflow-y-auto rounded-lg border p-2"
  >
    <p v-if="roles.length === 0" class="p-2 text-sm text-muted-foreground">暂无可分配的角色。</p>
    <label
      v-for="role in roles"
      :key="role.roleId"
      class="flex items-start gap-3 rounded-md p-2 hover:bg-muted/50"
    >
      <NvCheckbox
        :id="`${idPrefix}-${role.roleId}`"
        :model-value="selectedRoleIds.includes(role.roleId ?? '')"
        class="mt-0.5"
        @update:model-value="setSelected(role.roleId ?? '', $event)"
      />
      <span class="grid gap-0.5">
        <span class="text-sm font-medium">{{ role.roleName || role.roleId }}</span>
        <span class="text-xs text-muted-foreground">
          {{ role.permissionCodes?.length ?? 0 }} 项权限
        </span>
      </span>
    </label>
  </div>
</template>
