---
layout: page
title: NvNumberKeyboard 数字键盘
---

<script setup>
import { ref } from 'vue'
import { NvNumberKeyboard, NvMobileButton } from '@nerv-iip/ui-mobile'

const qty = ref('')
const qtyShow = ref(false)

const worker = ref('')
const workerShow = ref(false)
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">录入数量（带小数点）</p>
    <div
      class="flex h-11 items-center justify-between rounded-xl border border-border bg-card px-3"
      @click="qtyShow = true"
    >
      <span class="text-sm text-muted-foreground">完工数量</span>
      <span class="text-base font-medium tabular-nums text-foreground">
        {{ qty || '点此录入' }}
      </span>
    </div>
    <NvNumberKeyboard v-model="qty" v-model:show="qtyShow" title="录入完工数量" extra-key="." />
  </section>
  <section>
    <p class="ds-mdoc-label">录入工号（纯数字、无小数点）</p>
    <div
      class="flex h-11 items-center justify-between rounded-xl border border-border bg-card px-3"
      @click="workerShow = true"
    >
      <span class="text-sm text-muted-foreground">操作工号</span>
      <span class="text-base font-medium tabular-nums text-foreground">
        {{ worker || '点此录入' }}
      </span>
    </div>
    <NvNumberKeyboard
      v-model="worker"
      v-model:show="workerShow"
      title="录入操作工号"
      extra-key=""
      :maxlength="6"
      confirm-text="确定"
    />
  </section>
</template>

# NvNumberKeyboard 数字键盘

PDA 屏幕数字键盘（Arco 表单形态）。底部固定面板由 `v-model:show` 控制升降，大触控键适配戴手套操作。编辑字符串 `v-model`，每次按键同时触发 `press` 事件。用于录入数量、工号、称重等场景。右侧手机模拟器为实时组件，点击输入区域唤起键盘。

## 录入数量

`extra-key` 为左下角按键，默认 `.`（小数点，自动去重）。

```vue
<NvNumberKeyboard v-model="qty" v-model:show="show" title="录入完工数量" extra-key="." />
```

## 录入工号

设 `extra-key=""` 隐藏小数点，`0` 键自动加宽占满；`maxlength` 限制位数。

```vue
<NvNumberKeyboard
  v-model="worker"
  v-model:show="show"
  extra-key=""
  :maxlength="6"
  confirm-text="确定"
/>
```

## 属性

| 属性           | 说明                              | 类型      | 默认     |
| -------------- | --------------------------------- | --------- | -------- |
| `v-model`      | 录入的字符串                      | `string`  | `''`     |
| `v-model:show` | 面板是否展开                      | `boolean` | `false`  |
| `title`        | 面板标题                          | `string`  | `请输入` |
| `extraKey`     | 左下角按键，`''` 时隐藏并加宽 `0` | `string`  | `.`      |
| `maxlength`    | 最大字符数                        | `number`  | —        |
| `confirmText`  | 确认按钮文案                      | `string`  | `完成`   |

## 事件

| 事件      | 说明                        | 回调参数        |
| --------- | --------------------------- | --------------- |
| `press`   | 任意按键按下（含 `delete`） | `(key: string)` |
| `confirm` | 点击确认                    | —               |

</MobileDoc>
