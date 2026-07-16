# Button (NvButton)

Triggers an action or event. App code uses `NvButton` from `@nerv-iip/ui` (the
un-prefixed `Button` is the shadcn 原版 primitive — library-internal only, per
ADR 0020).

## Variants

| Variant       | Use case                                                                                  |
| ------------- | ----------------------------------------------------------------------------------------- |
| `default`     | 容器内主操作（对话框/抽屉确认、卡片内动作，近黑）                                         |
| `brand`       | **页面主 CTA 常规使用**（工具栏新建、表单提交；每页/每工具栏唯一，owner 裁决 2026-07-16） |
| `outline`     | Secondary actions (the most common non-primary variant)                                   |
| `ghost`       | Icon-only row actions, low-emphasis inline actions                                        |
| `destructive` | Irreversible destructive action (must be inside an NvAlertDialog confirm)                 |
| `secondary`   | Low-emphasis secondary action                                                             |
| `link`        | Inline text action styled as a link                                                       |

## Sizes

| Size      | Use case                                            |
| --------- | --------------------------------------------------- |
| `default` | Standard buttons in toolbars and forms              |
| `sm`      | Compact contexts, dense toolbars                    |
| `lg`      | Rarely used; prominent hero actions only            |
| `icon`    | Square icon-only button (always add `aria-label`)   |
| `icon-sm` | Compact square icon-only button (table row actions) |

## Loading

`NvButton` has a built-in `loading` prop (renders an `NvLoader` ring and sets
`aria-busy`) — do not hand-compose a `Spinner` inside the button.

## Usage

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

## Do NOT

- Do not use `variant="default"` and `variant="destructive"` side-by-side without an NvAlertDialog wrapping the destructive action.
- Do not use `type="button"` inside a `<form>` submit handler — use `type="submit"`.
- Do not create icon-only buttons without `aria-label`.
- Do not use `variant="link"` for navigation to another route — use `<RouterLink>`.
- Do not import the un-prefixed `Button` in app code — that is the 原版 primitive.
