# Badge (NvBadge / NvStatusBadge)

Label pills. Two brand components split the old Badge's jobs:

- **`NvBadge`** — category / type / count labels.
- **`NvStatusBadge`** — entity status (dot + tinted pill, shared status map,
  optional live `pulse`). Always prefer it for status columns.

Both come from `@nerv-iip/ui`. The un-prefixed `Badge` is the shadcn 原版
primitive — library-internal only.

## NvBadge Variants

| Variant             | Use case                                     |
| ------------------- | -------------------------------------------- |
| `neutral` (default) | Neutral category, type tag                   |
| `solid`             | Primary system label needing strong emphasis |
| `brand`             | Brand-tinted highlight label                 |
| `success`           | Positive label (non-status contexts)         |
| `warning`           | At-risk / attention label                    |
| `danger`            | Error / deletion-related label               |

> Note: the old 原版 variant names `secondary` / `outline` / `destructive` /
> `ghost` do not exist on `NvBadge` — use `neutral` for quiet tags and `danger`
> for error tones.

## NvStatusBadge

Props: `value` (raw status string, resolved to label + tone via the shared
`resolveStatus` map), `label` (override), `tone`
(`success | warning | danger | info | neutral`), `pulse` (live dot for active
states).

## Usage

```vue
<!-- Entity status — always NvStatusBadge with a semantic tone, never handcraft colors -->
<NvStatusBadge value="enabled" />
<NvStatusBadge label="Running" tone="success" pulse />
<NvStatusBadge label="Suspended" tone="danger" />
<NvStatusBadge label="Pending" tone="warning" />

<!-- Category / type tag -->
<NvBadge>Admin</NvBadge>
<NvBadge variant="brand">New</NvBadge>
```

## Do NOT

- Do not pass raw Tailwind classes like `class="border-emerald-200 bg-emerald-50 text-emerald-700"` — use a semantic variant/tone.
- Do not use `variant="destructive"` — the danger tone is named `danger` on both components.
- Do not use `NvBadge` for entity status columns — use `NvStatusBadge` so labels/tones stay consistent with the shared status map.
- Do not use a badge for text longer than 3 words.
