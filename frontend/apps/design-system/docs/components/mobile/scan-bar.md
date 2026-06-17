---
title: ScanBar 扫码栏
---

<script setup>
import { Cell, CellGroup, ScanBar } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const scans = ref(['MTL-7782-0034'])
function onScan(value) {
  scans.value.unshift(value)
}
</script>

# ScanBar 扫码栏

适配键盘楔入式扫码枪的输入栏：输入框常驻焦点，扫码枪以回车结束后触发 `scan` 事件。当上层打开浮层（抽屉 / 对话框）时传 `active=false`，让其停止自动抢焦点。

## 基础用法

<Demo mobile>
  <div style="display:flex;flex-direction:column;gap:12px;width:100%">
    <ScanBar placeholder="对准条码 / 二维码" @scan="onScan" />
    <CellGroup>
      <Cell v-for="(s, i) in scans" :key="`${s}-${i}`" :title="s" note="物料条码" />
    </CellGroup>
  </div>
</Demo>

```vue
<script setup>
const scans = ref(['MTL-7782-0034'])
function onScan(value) {
  scans.value.unshift(value)
}
</script>

<template>
  <ScanBar placeholder="对准条码 / 二维码" @scan="onScan" />
  <CellGroup>
    <Cell v-for="(s, i) in scans" :key="`${s}-${i}`" :title="s" note="物料条码" />
  </CellGroup>
</template>
```

## 暂停抢焦点

打开浮层时传 `:active="false"`，避免把焦点从浮层抢回、破坏 focus-trap。

```vue
<ScanBar :active="!sheetOpen" @scan="onScan" />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `placeholder` | 占位文本 | `string` | `'扫描条码 / 二维码'` |
| `active` | 是否自动重聚焦（浮层打开时设为 `false`） | `boolean` | `true` |

| 事件 | 说明 | 回调参数 |
|---|---|---|
| `scan` | 扫码枪回车提交一段条码 | `(value: string)` |
