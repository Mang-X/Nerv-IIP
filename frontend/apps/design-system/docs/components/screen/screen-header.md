---
title: ScreenHeader 大屏页头
---

<script setup>
import { ScreenHeader } from '@nerv-iip/ui'
</script>

# ScreenHeader 大屏页头

大屏顶部的页头横条:左侧标题,右侧一组工具(时钟 / 星期 / 产线筛选 / 大屏名,各为一枚 lucide 图标加短标签,末尾收一个菜单图标),坐在一条底部发丝线上。纯属性驱动 —— 任一工具值缺省即自动隐去,留一个标题也成立。

## 基础用法

横向占满整列,填入真实标题与挂钟时间。

<ScreenDemo wide>
  <ScreenHeader
    title="智能工厂 MES 运营看板"
    time="2024-06-12 10:24:36"
    date="星期三"
    line="全部产线"
    screen="中央控制室大屏 01"
  />
</ScreenDemo>

```vue
<ScreenHeader
  title="智能工厂 MES 运营看板"
  time="2024-06-12 10:24:36"
  date="星期三"
  line="全部产线"
  screen="中央控制室大屏 01"
/>
```

## 精简工具区

按需只传部分工具,缺省项自动隐去 —— 这里只保留时间与当前产线。

<ScreenDemo wide>
  <ScreenHeader
    title="焊接车间 · 产线监控"
    time="2024-06-12 10:24:36"
    line="焊接线 A"
    date=""
    screen=""
  />
</ScreenDemo>

```vue
<!-- date / screen 传空串即隐去对应工具 -->
<ScreenHeader
  title="焊接车间 · 产线监控"
  time="2024-06-12 10:24:36"
  line="焊接线 A"
  date=""
  screen=""
/>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `title` | 页头标题 | `string` | `'智能工厂 MES 运营看板'` |
| `time` | 挂钟时间(传空串则隐去该工具) | `string` | `'2024-06-12 10:24:36'` |
| `date` | 日期 / 星期(传空串则隐去) | `string` | `'星期三'` |
| `line` | 当前产线筛选(传空串则隐去) | `string` | `'全部产线'` |
| `screen` | 大屏 / 工位名(传空串则隐去) | `string` | `'中央控制室大屏 01'` |
