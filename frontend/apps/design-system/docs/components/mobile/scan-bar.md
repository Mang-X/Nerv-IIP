---
layout: page
title: NvScanBar 扫码栏
---

<script setup>
import { NvCell, NvCellGroup, NvScanBar } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const scans = ref(['MTL-7782-0034'])
function onScan(value) {
  scans.value.unshift(value)
}
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <div style="display:flex;flex-direction:column;gap:12px;width:100%">
      <NvScanBar placeholder="对准条码 / 二维码" @scan="onScan" />
      <NvCellGroup>
        <NvCell v-for="(s, i) in scans" :key="`${s}-${i}`" :title="s" note="物料条码" />
      </NvCellGroup>
    </div>
  </section>
</template>

# NvScanBar 扫码栏

适配键盘楔入式扫码枪的输入栏：输入框常驻焦点，扫码枪以回车结束后触发 `scan` 事件。当上层打开浮层（抽屉 / 对话框）时传 `active=false`，让其停止自动抢焦点。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

输入框常驻焦点，扫码枪回车后追加一条记录。

```vue
<script setup>
const scans = ref(['MTL-7782-0034'])
function onScan(value) {
  scans.value.unshift(value)
}
</script>

<template>
  <NvScanBar placeholder="对准条码 / 二维码" @scan="onScan" />
  <NvCellGroup>
    <NvCell v-for="(s, i) in scans" :key="`${s}-${i}`" :title="s" note="物料条码" />
  </NvCellGroup>
</template>
```

## 暂停抢焦点

打开浮层时传 `:active="false"`，避免把焦点从浮层抢回、破坏 focus-trap。

```vue
<NvScanBar :active="!sheetOpen" @scan="onScan" />
```

## 属性

| 属性          | 说明                                     | 类型      | 默认                  |
| ------------- | ---------------------------------------- | --------- | --------------------- |
| `placeholder` | 占位文本                                 | `string`  | `'扫描条码 / 二维码'` |
| `active`      | 是否自动重聚焦（浮层打开时设为 `false`） | `boolean` | `true`                |

| 事件   | 说明                   | 回调参数          |
| ------ | ---------------------- | ----------------- |
| `scan` | 扫码枪回车提交一段条码 | `(value: string)` |

</MobileDoc>
