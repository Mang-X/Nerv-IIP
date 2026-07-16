# Checkbox (NvCheckbox)

Boolean toggle or multi-select list item. App code uses `NvCheckbox` from
`@nerv-iip/ui` (reka `CheckboxRoot` API: plain `v-model` / `modelValue`, plus
`indeterminate` support). The un-prefixed `Checkbox` is the shadcn 原版
primitive — library-internal only.

## Usage

```vue
<!-- Standalone with label (the NvField pattern) -->
<NvField orientation="horizontal">
  <NvCheckbox id="terms" v-model="accepted" />
  <NvFieldLabel for="terms" class="leading-5">
    I accept the terms and conditions
  </NvFieldLabel>
</NvField>

<!-- Permission list (externally managed selection set) -->
<label
  v-for="permission in permissions"
  :key="permission.code"
  class="flex items-start gap-3 rounded-md p-2 hover:bg-muted/50"
>
  <NvCheckbox
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

Prefer plain `v-model` for simple booleans (the old `v-model:checked` spelling
is gone — the model prop is `modelValue`). Use `:model-value` +
`@update:model-value` when the state is managed externally (e.g. inside a
computed Set). An `indeterminate` model value renders the mixed state — use it
for "select all" headers over a partial selection.

## Do NOT

- Do not use `<input type="checkbox">` directly.
- Do not use `NvCheckbox` for exclusive single-choice selection — use `NvSelect` or `NvRadioGroup`.
- Do not put a checkbox inside an `NvDropdownMenuCheckboxItem` for primary form inputs.
