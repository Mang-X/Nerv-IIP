<script setup lang="ts">
import type { ConsoleIamPermissionResponse } from '@nerv-iip/api-client'
import PermissionCodeBadge from '@/components/iam/PermissionCodeBadge.vue'
import { Checkbox, Field, FieldGroup, FieldLabel, Input } from '@nerv-iip/ui'
import { computed, shallowRef } from 'vue'

const props = defineProps<{
  permissions: ConsoleIamPermissionResponse[]
}>()

const selectedCodes = defineModel<string[]>({ default: [] })
const search = shallowRef('')

const sortedPermissions = computed(() =>
  props.permissions
    .filter(permission => Boolean(permission.code))
    .toSorted((left, right) => {
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
    return [
      permission.code,
      permission.domain,
      permission.description,
    ].some(value => value?.toLowerCase().includes(query))
  })
})

const permissionGroups = computed(() => {
  const groups = new Map<string, ConsoleIamPermissionResponse[]>()

  for (const permission of filteredPermissions.value) {
    const domain = permission.domain || 'Other'
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
  }
  else {
    nextCodes.delete(code)
  }

  selectedCodes.value = [...nextCodes].sort()
}
</script>

<template>
  <FieldGroup class="gap-4">
    <Field>
      <FieldLabel for="iam-permission-search">Search permissions</FieldLabel>
      <Input
        id="iam-permission-search"
        v-model="search"
        placeholder="Search permission code, domain, or description"
        type="search"
      />
    </Field>

    <div class="flex items-center justify-between gap-3 text-sm">
      <span class="text-muted-foreground">{{ selectedCount }} selected</span>
      <div class="flex flex-wrap justify-end gap-1.5">
        <PermissionCodeBadge
          v-for="code in selectedCodes"
          :key="code"
          :code="code"
        />
      </div>
    </div>

    <div v-if="permissionGroups.length === 0" class="rounded-lg border p-4 text-sm text-muted-foreground">
      No permissions match the current search.
    </div>

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
          :for="`iam-permission-${permission.code}`"
        >
          <Checkbox
            :id="`iam-permission-${permission.code}`"
            :checked="isSelected(permission.code ?? '')"
            class="mt-0.5"
            @update:checked="setSelected(permission.code ?? '', $event)"
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
  </FieldGroup>
</template>
