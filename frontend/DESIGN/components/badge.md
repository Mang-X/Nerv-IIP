# Badge

Status and category label pill.

## Variants

| Variant | Use case |
|---|---|
| `default` | Primary system label, info state |
| `secondary` | Disabled, inactive, archived |
| `outline` | Neutral category, type tag |
| `destructive` | Error state, deletion-related |
| `success` | Active, enabled, healthy |
| `warning` | Degraded, at-risk, pending |
| `ghost` | De-emphasized label |

## Usage

```vue
<!-- Status badge — always use a named variant, never handcraft colors -->
<Badge variant="success">Enabled</Badge>
<Badge variant="secondary">Disabled</Badge>
<Badge variant="destructive">Suspended</Badge>
<Badge variant="warning">Pending</Badge>

<!-- Category / type tag -->
<Badge variant="outline">Admin</Badge>
```

## Do NOT

- Do not pass raw Tailwind classes like `class="border-emerald-200 bg-emerald-50 text-emerald-700"` — use `variant="success"`.
- Do not use `default` (blue) for success states.
- Do not use `destructive` for warning states.
- Do not use Badge for text longer than 3 words.
