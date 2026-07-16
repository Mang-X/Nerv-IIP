# Block: Page Header（页头）

> **现役实现 = `NvPageHeader`**（`@nerv-iip/ui` blocks 层，
> `packages/ui/src/components/blocks/page-header/PageHeader.vue`）。本文早期版本描述的
> `IamPageHeader`（大标题 `h1` + 描述段）**已不存在**——被「面包屑即标题」的
> `NvPageHeader` 取代；历史约定存档于文末附录。

## 规则

1. **每页一个 `NvPageHeader`**，紧凑形态：面包屑即标题（`title` 渲染为最后一级面包屑）+
   可选 `count`（结果总数）+ `#actions`（右对齐行内动作，如［刷新］［+ 新建 X］）。
2. **不再做大标题 + 描述块**：页面用途靠 标题/count/列头/徽标/空态 表达，不靠成段解说
   （见 `pages/list-workbench.md` 的「Guide with UI, not prose」）。
3. `count` 用**服务端总量**（不是当前分页页内条数），格式如 `` `${totalCount} 个用户` ``。
4. 祖先层级走 `breadcrumbs`（`{ label, href? }[]`；无 `href` 即不可点的域名段）；SPA
   路由链接用 `#breadcrumbs` 槽自渲染。
5. 组件自带 sticky 定位（`top-14`）与贴边负 margin，**不要**再包 Card 或额外容器。
6. 页头 `#actions` 放页面级主动作；行级动作归表格尾列（`blocks/data-table.md`）。

## 判定

- 「标题是面包屑最后一级、还是又写了一个独立大标题 `h1` + 描述段？」后者 → 打回改
  `NvPageHeader`。
- 「count 是服务端总量吗？」本页行数/机械计数 → 打回（误导）。
- 「这个动作是页面级（新建/刷新/导出）还是行级？」行级动作不进页头。

## 正例

`apps/console/src/pages/iam/users/index.vue:253`（现网真实用法）：

```vue
<NvPageHeader title="用户" :breadcrumbs="[{ label: '身份与访问' }]" :count="`${totalCount} 个用户`">
  <template #actions>
    <Button type="button" :disabled="!canManageUsers" @click="openCreateDialog">新建用户</Button>
  </template>
</NvPageHeader>
```

另见黄金标准页 `apps/business-console/src/pages/mes/operation-tasks.vue:300`
（`NvPageHeader` + `#actions` 放刷新）。

<!-- 反例：暂无现网证据——大标题+描述块的旧形态已在区块迁移中清除，黄金标准契约测试强制 NvPageHeader。 -->

---

## 附录：`IamPageHeader` 旧形态（组件已删除）

旧组件 `apps/console/src/components/iam/IamPageHeader.vue` 渲染
`h1.text-2xl.font-semibold` 大标题 + `p.text-sm.text-muted-foreground` 描述段。
当时的可保留决策（升入现规范的部分）：页级标题唯一（一页一个页头）、页头不进 Card、
主动作与页头同排右置（现由 `#actions` 槽承载）。「不得省略描述段」的旧规则已被规则 2
（UI 引导而非解说）取代。
