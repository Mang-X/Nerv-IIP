# Field (NvField)

包含标签、说明和错误消息的表单字段包装器。应用代码使用 `NvField*` 系列，
它们来自 `@nerv-iip/ui`；无前缀的 `Field*` 部件是 shadcn 原版
primitives，仅限组件库内部使用。

## 结构

```
NvField (or NvFieldSet for groups)
  NvFieldLabel
  NvFieldGroup (optional: grouping wrapper)
    NvInput / NvSelect / NvCheckbox
  NvFieldDescription (optional: help text)
  NvFieldError (conditional: validation error)
```

## 用法

```vue
<!-- Simple field -->
<NvField>
  <NvFieldLabel for="email">Email address</NvFieldLabel>
  <NvInput id="email" v-model="form.email" type="email" required />
  <NvFieldDescription>Used for login and notifications.</NvFieldDescription>
  <NvFieldError v-if="errors.email">{{ errors.email }}</NvFieldError>
</NvField>

<!-- Field with icon prefix -->
<NvField>
  <NvFieldLabel for="search">Search</NvFieldLabel>
  <div class="relative">
    <SearchIcon class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
    <NvInput id="search" v-model="search" class="pl-8" type="search" />
  </div>
</NvField>

<!-- Checkbox inside NvFieldSet -->
<NvFieldSet>
  <NvFieldLegend>Permissions</NvFieldLegend>
  <NvFieldGroup class="gap-2">
    <NvField v-for="p in permissions" :key="p.code" orientation="horizontal">
      <NvCheckbox :id="p.code" v-model="selected[p.code]" />
      <NvFieldLabel :for="p.code">{{ p.code }}</NvFieldLabel>
    </NvField>
  </NvFieldGroup>
</NvFieldSet>
```

## 堆叠布局

表单字段应以 `gap-4` 的间距堆叠在 `NvFieldGroup` 或
`<form class="grid gap-4">` 中。`NvField` 支持 `orientation="vertical | horizontal | responsive"`
（vertical 为默认值）。

## 禁止

- 不得使用原始 `<label>` 元素；必须始终使用 `NvFieldLabel`。
- 不得使用原始 `<p>` 展示错误文本；必须始终使用 `NvFieldError`。
- 不得将 `NvFieldError` 放在 `NvField` 上下文之外。
- 不得省略 `for` / `id` 与 `NvFieldLabel` 及输入控件之间的配对。
