# Checkbox

Boolean toggle or multi-select list item.

## Usage

```vue
<!-- Standalone with label (the Field pattern) -->
<Field class="flex flex-row items-start gap-3">
  <Checkbox id="terms" v-model:checked="accepted" />
  <FieldLabel for="terms" class="leading-5">
    I accept the terms and conditions
  </FieldLabel>
</Field>

<!-- Permission list (the RolePermissionEditor pattern) -->
<label
  v-for="permission in permissions"
  :key="permission.code"
  class="flex items-start gap-3 rounded-md p-2 hover:bg-muted/50"
>
  <Checkbox
    :id="`perm-${permission.code}`"
    :model-value="isSelected(permission.code)"
    class="mt-0.5"
    @update:model-value="toggle(permission.code, $event)"
  />
  <span class="grid gap-1">
    <span class="font-mono text-sm">{{ permission.code }}</span>
    <span v-if="permission.description" class="text-sm text-muted-foreground">
      {{ permission.description }}
    </span>
  </span>
</label>
```

## Controlled vs v-model

Prefer `v-model:checked` for simple booleans. Use `:model-value` + `@update:model-value` when the state is managed externally (e.g. inside a computed Set).

## Do NOT

- Do not use `<input type="checkbox">` directly.
- Do not use Checkbox for exclusive single-choice selection — use `Select` or radio buttons.
- Do not put Checkbox inside a `DropdownMenuCheckboxItem` for primary form inputs.
