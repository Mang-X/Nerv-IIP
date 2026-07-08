---
title: NvScreenPagination 分页
---

<script setup>
import { NvScreenPagination, NvScreenPanel } from '@nerv-iip/ui'
import { ref } from 'vue'

const page = ref(1)
const page2 = ref(6)
</script>

# NvScreenPagination 分页

大屏数据表格的分页器:总数 + 当前区间读数、上一页 / 下一页、带省略号的页码窗口(`1 … p-1 p p+1 … N`)。当前页青色填充,控件按下纯缩放、无位移。`v-model:page` 驱动当前页,`total` + `pageSize` 推算页数。

## 基础用法

<ScreenDemo wide>
  <NvScreenPanel>
    <NvScreenPagination v-model:page="page" :total="248" :page-size="10" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvScreenPagination v-model:page="page" :total="248" :page-size="10" />
```

## 省略号窗口

页数较多时,中间区段用省略号收起,首尾页常驻。

<ScreenDemo wide>
  <NvScreenPanel>
    <NvScreenPagination v-model:page="page2" :total="1280" :page-size="20" />
  </NvScreenPanel>
</ScreenDemo>

## 属性

| 属性           | 说明     | 类型     | 默认 |
| -------------- | -------- | -------- | ---- |
| `v-model:page` | 当前页   | `number` | `1`  |
| `total`        | 总条数   | `number` | —    |
| `pageSize`     | 每页条数 | `number` | `10` |
