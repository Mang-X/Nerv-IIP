# Badge (NvBadge / NvStatusBadge)

标签胶囊。两个品牌组件分担旧版 Badge 的职责：

- **`NvBadge`** — 类别、类型或数量标签。
- **`NvStatusBadge`** — 实体状态（圆点加浅色胶囊、共享状态映射，
  可选实时 `pulse`）。状态列始终优先使用它。

两者均从 `@nerv-iip/ui` 导出。无前缀的 `Badge` 是 shadcn 原版
基础组件（primitive），仅限组件库内部使用。

## NvBadge 变体

| 变体              | 使用场景                       |
| ----------------- | ------------------------------ |
| `neutral`（默认） | 中性类别、类型标签             |
| `solid`           | 需要强强调的主系统标签         |
| `brand`           | 品牌色高亮标签                 |
| `success`         | 正向标签（非状态场景）         |
| `warning`         | 有风险或需要关注的标签         |
| `danger`          | 错误或与删除相关的标签         |

> 注意：旧版原版的变体名称 `secondary` / `outline` / `destructive` /
> `ghost` 不存在于 `NvBadge`；低强调标签请使用 `neutral`，错误色调请使用
> `danger`。

## NvStatusBadge

属性：`value`（原始状态字符串，通过共享的 `resolveStatus` 映射解析为标签与色调）、
`label`（覆盖值）、`tone`（`success | warning | danger | info | neutral`）、
`pulse`（用于活跃状态的实时圆点）。

## 用法

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

## 禁止

- 不得传入 `class="border-emerald-200 bg-emerald-50 text-emerald-700"` 等原始 Tailwind 类；应使用语义化变体或色调。
- 不得使用 `variant="destructive"`；两个组件中的危险色调均命名为 `danger`。
- 不得在实体状态列使用 `NvBadge`；应使用 `NvStatusBadge`，使标签和色调与共享状态映射保持一致。
- 不得将标签用于超过 3 个词的文本。
