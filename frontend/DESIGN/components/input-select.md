# 输入 / 选择器 (NvInput / NvSelect)

文本输入和固定选项选择器。在表单中必须置于 `NvField` 上下文内使用。应用代码使用
`NvInput` 和 `NvSelect*` 家族，均来自 `@nerv-iip/ui`；无前缀的 `Input` / `Select*` 是
shadcn 原版 primitive，仅限库内部使用。

## 输入框

```vue
<!-- Plain text -->
<NvField>
  <NvFieldLabel for="login-name">Login name</NvFieldLabel>
  <NvInput id="login-name" v-model="form.loginName" required />
  <NvFieldError v-if="errors.loginName">{{ errors.loginName }}</NvFieldError>
</NvField>

<!-- Search with icon prefix -->
<div class="relative">
  <SearchIcon
    class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
    aria-hidden="true"
  />
  <NvInput v-model="search" class="pl-8" type="search" placeholder="Search…" />
</div>

<!-- Password -->
<NvInput v-model="form.password" type="password" autocomplete="current-password" />
```

## 选择器

```vue
<NvField>
  <NvFieldLabel for="role-type">Role type</NvFieldLabel>
  <NvSelect v-model="form.roleType">
    <NvSelectTrigger id="role-type">
      <NvSelectValue placeholder="Choose a type…" />
    </NvSelectTrigger>
    <NvSelectContent>
      <NvSelectItem value="system">System</NvSelectItem>
      <NvSelectItem value="custom">Custom</NvSelectItem>
    </NvSelectContent>
  </NvSelect>
</NvField>

<!-- Toolbar filter (no NvField wrapper needed) -->
<NvSelect v-model="statusFilter">
  <NvSelectTrigger class="w-36" aria-label="Filter by status">
    <NvSelectValue placeholder="Status" />
  </NvSelectTrigger>
  <NvSelectContent>
    <NvSelectItem value="all">All statuses</NvSelectItem>
    <NvSelectItem value="enabled">Enabled</NvSelectItem>
    <NvSelectItem value="disabled">Disabled</NvSelectItem>
  </NvSelectContent>
</NvSelect>
```

桌面端产品 UI 使用 `NvSelect`。使用方可以传入宽度、紧凑高度等仅影响布局的 class（样式类），
但不得重设触发器、内容和选项的颜色；这些状态属于组件契约。Reka 运行时约束：
`NvSelectItem` 的 `value` 不得为空字符串。

## 输入类型

| 类型       | 使用场景                                                |
| ---------- | ------------------------------------------------------- |
| `text`     | 默认类型                                                |
| `email`    | 电子邮箱地址（启用浏览器校验）                          |
| `password` | 凭据（始终添加 `autocomplete`）                         |
| `search`   | 搜索输入（多数浏览器会渲染清除按钮）                    |
| `number`   | 整数数量                                                |

## 禁止事项

- 不得直接使用 `<input>`，必须使用 `<NvInput>`，后者来自 `@nerv-iip/ui`。
- 不得用 `NvInput` 从固定列表中选择，应使用 `NvSelect`。
- 不得用 `NvSelect` 搜索大型数据集，应使用 `NvSearchSelect`（可搜索的弹窗单选）或 `NvCombobox`（输入筛选并允许自由输入），两者均来自 `@nerv-iip/ui`。
- 不得省略 `for`/`id` 配对（位于 `NvField` 内时）。
