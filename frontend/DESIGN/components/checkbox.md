# 复选框（NvCheckbox）

布尔切换控件或多选列表项。应用代码使用 `NvCheckbox`（来自 `@nerv-iip/ui`；
reka `CheckboxRoot` API：直接使用 `v-model` / `modelValue`，并支持
`indeterminate`）。无前缀的 `Checkbox` 是 shadcn 原版 primitive，
仅限组件库内部使用。

## 用法

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

## 受控模式与 v-model

简单布尔值应优先使用普通 `v-model`（旧的 `v-model:checked` 写法已移除，
模型属性为 `modelValue`）。状态由外部管理时（例如计算所得的 Set），使用
`:model-value` + `@update:model-value`。`indeterminate` 模型值会渲染
混合状态，适用于部分选中时的“全选”表头。

## 禁止

- 不得直接使用 `<input type="checkbox">`。
- 不得将 `NvCheckbox` 用于互斥的单选；应使用 `NvSelect` 或 `NvRadioGroup`。
- 不得将复选框放入 `NvDropdownMenuCheckboxItem`，作为主要表单输入。
