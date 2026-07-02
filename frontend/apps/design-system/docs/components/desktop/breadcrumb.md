---
title: Breadcrumb 面包屑
---

<script setup>
import {
  Breadcrumb, BreadcrumbList, BreadcrumbItem, BreadcrumbLink, BreadcrumbPage,
  BreadcrumbSeparator, BreadcrumbEllipsis,
} from '@nerv-iip/ui'
</script>

# Breadcrumb 面包屑

显示当前页面在层级中的位置并支持快速回退。末项用 `BreadcrumbPage`（当前页、不可点），其余用 `BreadcrumbLink`。

## 基础

<Demo>
  <Breadcrumb>
    <BreadcrumbList>
      <BreadcrumbItem><BreadcrumbLink href="#">控制台</BreadcrumbLink></BreadcrumbItem>
      <BreadcrumbSeparator />
      <BreadcrumbItem><BreadcrumbLink href="#">工单</BreadcrumbLink></BreadcrumbItem>
      <BreadcrumbSeparator />
      <BreadcrumbItem><BreadcrumbPage>WO-2406-0413</BreadcrumbPage></BreadcrumbItem>
    </BreadcrumbList>
  </Breadcrumb>
</Demo>

```vue
<Breadcrumb>
  <BreadcrumbList>
    <BreadcrumbItem><BreadcrumbLink href="/">控制台</BreadcrumbLink></BreadcrumbItem>
    <BreadcrumbSeparator />
    <BreadcrumbItem><BreadcrumbPage>WO-2406-0413</BreadcrumbPage></BreadcrumbItem>
  </BreadcrumbList>
</Breadcrumb>
```

## 折叠中间层级

层级很深时用 `BreadcrumbEllipsis` 折叠中间项。

<Demo>
  <Breadcrumb>
    <BreadcrumbList>
      <BreadcrumbItem><BreadcrumbLink href="#">控制台</BreadcrumbLink></BreadcrumbItem>
      <BreadcrumbSeparator />
      <BreadcrumbItem><BreadcrumbEllipsis /></BreadcrumbItem>
      <BreadcrumbSeparator />
      <BreadcrumbItem><BreadcrumbLink href="#">WC-CNC-07</BreadcrumbLink></BreadcrumbItem>
      <BreadcrumbSeparator />
      <BreadcrumbItem><BreadcrumbPage>工序 OP-03</BreadcrumbPage></BreadcrumbItem>
    </BreadcrumbList>
  </Breadcrumb>
</Demo>

## 组成

| 部件 | 说明 |
|---|---|
| `Breadcrumb` / `BreadcrumbList` | 根 + 列表（`<nav>` / `<ol>`，含 aria）|
| `BreadcrumbItem` | 单项容器 |
| `BreadcrumbLink` | 可点击层级（支持 `as-child` 接 router-link）|
| `BreadcrumbPage` | 当前页，不可点、`aria-current="page"` |
| `BreadcrumbSeparator` | 分隔符（默认 `/`，可自定义插槽）|
| `BreadcrumbEllipsis` | 折叠中间层级的省略号 |
