<script setup lang="ts">
import type { ConsoleIamPermissionResponse } from '@nerv-iip/api-client'
import PermissionCodeBadge from '@/components/iam/PermissionCodeBadge.vue'
import { Checkbox, Field, FieldGroup, FieldLabel, Input } from '@nerv-iip/ui'
import { computed, shallowRef } from 'vue'

const props = defineProps<{
  permissions: ConsoleIamPermissionResponse[]
}>()

const selectedCodes = defineModel<string[]>({ default: () => [] })
const search = shallowRef('')

const sortedPermissions = computed(() =>
  [...props.permissions]
    .filter((permission) => Boolean(permission.code))
    .sort((left, right) => {
      const domainCompare = (left.domain ?? '').localeCompare(right.domain ?? '')
      if (domainCompare !== 0) {
        return domainCompare
      }

      return (left.code ?? '').localeCompare(right.code ?? '')
    }),
)

const filteredPermissions = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (!query) {
    return sortedPermissions.value
  }

  return sortedPermissions.value.filter((permission) => {
    return [permission.code, permission.domain, permission.description].some((value) =>
      value?.toLowerCase().includes(query),
    )
  })
})

const permissionGroups = computed(() => {
  const groups = new Map<string, ConsoleIamPermissionResponse[]>()

  for (const permission of filteredPermissions.value) {
    const domain = permission.domain || '其他'
    groups.set(domain, [...(groups.get(domain) ?? []), permission])
  }

  return [...groups.entries()].map(([domain, permissions]) => ({
    domain,
    permissions,
  }))
})

const selectedCount = computed(() => selectedCodes.value.length)

function isSelected(code: string) {
  return selectedCodes.value.includes(code)
}

function setSelected(code: string, checked: boolean | 'indeterminate') {
  const nextCodes = new Set(selectedCodes.value)

  if (checked === true) {
    nextCodes.add(code)
  } else {
    nextCodes.delete(code)
  }

  selectedCodes.value = [...nextCodes].sort()
}
</script>

<template>
  <FieldGroup class="gap-4">
    <Field>
      <FieldLabel for="iam-permission-search">搜索权限</FieldLabel>
      <Input
        id="iam-permission-search"
        v-model="search"
        placeholder="搜索权限码、域或描述"
        type="search"
      />
    </Field>

    <div class="flex items-start justify-between gap-3 text-sm">
      <span class="text-muted-foreground">已选 {{ selectedCount }} 项</span>
      <div class="flex max-h-20 flex-wrap justify-end gap-1.5 overflow-y-auto">
        <PermissionCodeBadge v-for="code in selectedCodes" :key="code" :code="code" />
      </div>
    </div>

    <div
      v-if="permissionGroups.length === 0"
      class="rounded-lg border p-4 text-sm text-muted-foreground"
    >
      没有匹配的权限。
    </div>

    <div
      data-testid="role-permission-editor-scroll"
      class="grid max-h-[min(45vh,28rem)] gap-3 overflow-y-auto pr-1"
    >
      <section
        v-for="group in permissionGroups"
        :key="group.domain"
        class="grid gap-2 rounded-lg border p-3"
      >
        <h3 class="text-sm font-medium text-foreground">
          {{ group.domain }}
        </h3>
        <div class="grid gap-2">
          <label
            v-for="permission in group.permissions"
            :key="permission.code"
            class="flex items-start gap-3 rounded-md p-2 hover:bg-muted/50"
          >
            <Checkbox
              :id="`iam-permission-${permission.code}`"
              :model-value="isSelected(permission.code ?? '')"
              class="mt-0.5"
              @update:model-value="setSelected(permission.code ?? '', $event)"
            />
            <span class="grid gap-1">
              <span class="font-mono text-sm">{{ permission.code }}</span>
              <span v-if="permission.description" class="text-sm text-muted-foreground">
                {{ permission.description }}
              </span>
            </span>
          </label>
        </div>
      </section>
    </div>
  </FieldGroup>
</template>
