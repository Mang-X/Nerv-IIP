# Page: List Page（标准 CRUD 实体列表页）

标准 CRUD 实体列表页（ops console / IAM 管理一类）。拼装 = `NvPageHeader` +
`NvToolbar` + `NvDataTable` + 建档弹窗（`flows/create-dialog.md`）+ 破坏性确认
（`flows/confirm-destroy.md`）。

> Business Console 的业务列表页有更完整的基线 `pages/list-workbench.md`
> （BusinessLayout + KPI 判定 + 文案铁律 + 契约测试强制）；本文是 IAM/运维 console
> 的简化变体，区块拼装规则一致。

## 规则

1. **区块拼装**：页头 `NvPageHeader`（面包屑即标题 + 服务端总量 count + `#actions` 放
   ［新建 X］）；工具条 `NvToolbar`（搜索 + `#filters`）；表格 `NvDataTable`；状态列
   `NvStatusBadge`。不手搓裸表/裸页头。
2. **页面结构分四层**（script 组织约定）：① 数据 composable（查询/分页/mutations，如
   `useIamUsers`）→ ② 本地 UI 状态（search/筛选/弹窗开关/confirm target）→ ③ 派生
   computed（小数据集可客户端过滤）→ ④ 权限（`usePermissions` / `useHasPermission`）。
3. **弹窗单实例在页面层**：Create 弹窗与 `AlertDialog` 确认框声明在页面顶层、`v-for` 外。
4. **权限门控用 disabled**：无权限时按钮 `:disabled`，不隐藏、不放会失败的假按钮。
5. **布局**：页面内容为纵向 `grid gap-6`（或 `flex flex-col gap-6`）；间距是页面级关注点，
   不下沉进子组件重复；加载中不用 `v-show` 藏工具条。

## 判定

- 「页头/工具条/表格是不是区块，而不是手拼？」
- 「count 与分页 total 用的是服务端总量吗？」
- 「确认框/建档弹窗在页面层单实例吗？」
- 「无权限时动作是 disabled 还是消失/可点后失败？」

## 正例

现网参考实现（IAM 管理，全部已迁移到 Nv 区块）：

- `frontend/apps/console/src/pages/iam/users/index.vue`（`NvPageHeader:253` +
  `NvToolbar:265` + `NvDataTable` + `NvPagination` + `NvStatusBadge`；建档
  `components/iam/UserCreateDialog.vue`）
- `frontend/apps/console/src/pages/iam/roles/index.vue`
- `frontend/apps/console/src/pages/iam/sessions/index.vue`

结构示意（对应 users/index.vue 的真实形态）：

```vue
<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <NvPageHeader
        title="用户"
        :breadcrumbs="[{ label: '身份与访问' }]"
        :count="`${totalCount} 个用户`"
      >
        <template #actions>
          <Button type="button" :disabled="!canManageUsers" @click="openCreateDialog"
            >新建用户</Button
          >
        </template>
      </NvPageHeader>

      <NvToolbar :search="search" search-label="搜索用户" @update:search="search = $event">
        <template #filters><!-- 状态 Select --></template>
      </NvToolbar>

      <NvDataTable :columns="columns" :rows="rows" row-key="userId" :loading="pending">
        <template #cell-status="{ row }"
          ><NvStatusBadge :value="row.enabled ? 'active' : 'disabled'"
        /></template>
        <template #cell-actions="{ row }"><!-- 行操作，分级见 interaction-patterns §2 --></template>
      </NvDataTable>

      <!-- 建档弹窗 / 确认框：页面层单实例 -->
      <UserCreateDialog v-model:open="createOpen" @submit="onCreate" />
    </section>
  </DefaultLayout>
</template>
```

<!-- TODO(pattern): apps/console IAM 页仍混用原版 Button/Select/AlertDialog 与 Nv 区块（console 属运维面，未列入 Nv 全量迁移范围）；business 侧新页一律全 Nv*，console 侧新增代码建议同样用 Nv*。 -->

<!-- 反例：暂无现网证据（IAM 三页即基线本身；business 列表页的违例见 list-workbench.md 与 interaction-patterns.md）。 -->
