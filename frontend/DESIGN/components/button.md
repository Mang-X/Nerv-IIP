# Button

Triggers an action or event.

## Variants

| Variant | Use case |
|---|---|
| `default` | Primary call-to-action (max one per toolbar/form) |
| `outline` | Secondary actions |
| `ghost` | Icon-only row actions, navigation links |
| `destructive` | Irreversible destructive action (must be inside AlertDialog confirm) |
| `link` | Inline text navigation |
| `secondary` | Low-emphasis secondary action |

## Sizes

| Size | Use case |
|---|---|
| `default` | Standard buttons in toolbars and forms |
| `sm` | Compact contexts, dense toolbars |
| `icon` | Square icon-only button (always add `aria-label`) |
| `lg` | Rarely used; prominent hero actions only |

## Usage

```vue
<!-- Primary toolbar action -->
<Button type="button" @click="openCreateDialog">Create User</Button>

<!-- Secondary action -->
<Button variant="outline" type="button" @click="exportData">Export</Button>

<!-- Icon-only row action -->
<Button variant="ghost" size="icon" type="button" aria-label="Open actions for Alice">
  <MoreHorizontalIcon class="size-4" aria-hidden="true" />
</Button>

<!-- Inside a form — use type="submit" to avoid double-submission -->
<Button type="submit" :disabled="pending">Save changes</Button>

<!-- Destructive — ONLY inside AlertDialogAction, never standalone -->
<AlertDialogAction as-child>
  <Button variant="destructive" type="button">Delete user</Button>
</AlertDialogAction>
```

## Do NOT

- Do not use `variant="default"` and `variant="destructive"` side-by-side without an AlertDialog wrapping the destructive action.
- Do not use `type="button"` inside a `<form>` submit handler — use `type="submit"`.
- Do not create icon-only buttons without `aria-label`.
- Do not use `variant="link"` for navigation to another route — use `<RouterLink>`.
