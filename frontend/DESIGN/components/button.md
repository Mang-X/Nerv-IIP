# 按钮（NvButton）

触发操作或事件。应用代码应使用 `NvButton`，它由 `@nerv-iip/ui` 导入（无前缀的
`Button` 是 shadcn 原版基础组件（primitive）；依 ADR 0020，仅限组件库内部使用）。

## 变体

| 变体          | 使用场景                                                                            |
| ------------- | ----------------------------------------------------------------------------------- |
| `default`     | 容器内主操作（对话框/抽屉确认、卡片内动作，近黑）                                         |
| `brand`       | **页面主 CTA 常规使用**（工具栏新建、表单提交；每页/每工具栏唯一，负责人裁决 2026-07-16） |
| `outline`     | 次级操作（最常用的非主操作变体）                                                     |
| `ghost`       | 仅图标的行操作、低强调的行内操作                                                     |
| `destructive` | 不可逆的破坏性操作（必须置于 NvAlertDialog 确认中）                                   |
| `secondary`   | 低强调的次级操作                                                                     |
| `link`        | 呈现为链接样式的行内文本操作                                                         |

## 尺寸

| 尺寸      | 使用场景                                      |
| --------- | --------------------------------------------- |
| `default` | 工具栏和表单中的标准按钮                      |
| `sm`      | 紧凑场景、密集工具栏                          |
| `lg`      | 很少使用；仅用于突出的主视觉操作              |
| `icon`    | 方形仅图标按钮（始终添加 `aria-label`）       |
| `icon-sm` | 紧凑方形仅图标按钮（表格行操作）              |

## 加载中状态

`NvButton` 内置 `loading` prop（渲染 `NvLoader` 圆环并设置 `aria-busy`）；
不得在按钮内部手动组合 `Spinner`。

## 用法

```vue
<!-- Page-level primary CTA (toolbar) — brand, one per page/toolbar -->
<NvButton variant="brand" type="button" @click="openCreateDialog">Create User</NvButton>

<!-- Secondary action -->
<NvButton variant="outline" type="button" @click="exportData">Export</NvButton>

<!-- Icon-only row action -->
<NvButton variant="ghost" size="icon" type="button" aria-label="Open actions for Alice">
  <MoreHorizontalIcon class="size-4" aria-hidden="true" />
</NvButton>

<!-- Inside a form — type="submit" + built-in loading state -->
<NvButton type="submit" :loading="pending">Save changes</NvButton>

<!-- Destructive — ONLY inside NvAlertDialogAction, never standalone -->
<NvAlertDialogAction as-child>
  <NvButton variant="destructive" type="button">Delete user</NvButton>
</NvAlertDialogAction>
```

## 禁止

- 未使用 NvAlertDialog 包裹破坏性操作时，不得并列使用 `variant="default"` 和 `variant="destructive"`。
- 不得将 `type="button"` 用于 `<form>` 提交处理程序；应使用 `type="submit"`。
- 不得创建没有 `aria-label` 的仅图标按钮。
- 不得将 `variant="link"` 用于跳转至其他路由；应使用 `<RouterLink>`。
- 不得在应用代码中导入无前缀的 `Button`；它是原版基础组件（primitive）。
