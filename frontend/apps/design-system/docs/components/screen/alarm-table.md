---
title: AlarmTable 告警列表
---

<script setup>
import { NvAlarmTable } from '@nerv-iip/ui'

const rows = [
  { time: '10:23:14', line: 'CNC 线 C', level: 'sev', name: '主轴电机过载', wo: 'WO-2406-0421', status: '未确认' },
  { time: '10:18:07', line: '焊接线 A', level: 'gen', name: '焊枪温度超限', wo: 'WO-2406-0418', status: '处理中' },
  { time: '10:12:55', line: '装配线 B', level: 'gen', name: '物料短缺(缺料)', wo: 'WO-2406-0415', status: '处理中' },
  { time: '10:05:31', line: 'CNC 线 C', level: 'sev', name: '主轴振动异常', wo: 'WO-2406-0409', status: '待确认' },
  { time: '09:58:22', line: '焊接线 A', level: 'gen', name: '气压低于阈值', wo: 'WO-2406-0406', status: '已恢复' },
]
</script>

# AlarmTable 告警列表

大屏告警清单:极简表格(无竖线、发丝行线),以严重度圆点起首 —— 红点为「严重」,琥珀点为「一般」;工单号以等宽字体呈现,状态为「已恢复」时转绿。纯数据驱动,组件自带 `NvScreenPanel` 容器(含标题与「查看全部」入口),直接使用即可。横向铺满,放进 `<ScreenDemo wide>`。

## 基础用法

不传 `rows` 时使用内置示例。下例传入一组真实告警(温度超限 / 主轴振动 / 缺料)。

<ScreenDemo wide>
  <NvAlarmTable :rows="rows" />
</ScreenDemo>

```vue
<script setup>
import { NvAlarmTable } from '@nerv-iip/ui'

const rows = [
  {
    time: '10:23:14',
    line: 'CNC 线 C',
    level: 'sev',
    name: '主轴电机过载',
    wo: 'WO-2406-0421',
    status: '未确认',
  },
  {
    time: '10:18:07',
    line: '焊接线 A',
    level: 'gen',
    name: '焊枪温度超限',
    wo: 'WO-2406-0418',
    status: '处理中',
  },
  {
    time: '09:58:22',
    line: '焊接线 A',
    level: 'gen',
    name: '气压低于阈值',
    wo: 'WO-2406-0406',
    status: '已恢复',
  },
]
</script>

<NvAlarmTable :rows="rows" />
```

## 属性

| 属性   | 说明       | 类型         | 默认          |
| ------ | ---------- | ------------ | ------------- |
| `rows` | 告警行数组 | `AlarmRow[]` | 内置 5 行示例 |

### `AlarmRow` 行

| 字段     | 说明                                          | 类型         | 默认 |
| -------- | --------------------------------------------- | ------------ | ---- |
| `time`   | 告警时间,如「10:23:14」                       | `string`     | —    |
| `line`   | 产线名称                                      | `string`     | —    |
| `level`  | 告警级别:`sev` 严重(红点)/ `gen` 一般(琥珀点) | `sev \| gen` | —    |
| `name`   | 告警内容                                      | `string`     | —    |
| `wo`     | 工单号,如「WO-2406-0421」,等宽呈现            | `string`     | —    |
| `status` | 状态文本;值为「已恢复」时转绿                 | `string`     | —    |
