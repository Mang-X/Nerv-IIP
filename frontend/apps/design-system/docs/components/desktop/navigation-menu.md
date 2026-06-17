---
title: NavigationMenu 导航菜单
---

<script setup>
import {
  NavigationMenuPro,
  NavigationMenuProList,
  NavigationMenuProItem,
  NavigationMenuProTrigger,
  NavigationMenuProContent,
  NavigationMenuProLink,
  NavigationMenuProIndicator,
} from '@nerv-iip/ui'
import {
  ClipboardListIcon,
  CalendarRangeIcon,
  BarChart3Icon,
  WrenchIcon,
  GaugeIcon,
  ActivityIcon,
  ShieldCheckIcon,
  ScanLineIcon,
  SettingsIcon,
} from 'lucide-vue-next'
</script>

# NavigationMenu 导航菜单

横向控制台导航条：触发器展开下方的大型菜单面板（mega-menu），视口随面板内容动画改变大小与位置，指示器滑向当前激活项。简单入口用纯链接。`NavigationMenuPro` 家族在 reka 原语之上复制重建，仅采用我们的设计令牌，从不修改原版。

## 工厂控制台

将“生产 / 设备 / 质量”做成可展开面板，“设置”作为直达链接。

<Demo>
  <NavigationMenuPro>
    <NavigationMenuProList>
      <NavigationMenuProItem>
        <NavigationMenuProTrigger>生产</NavigationMenuProTrigger>
        <NavigationMenuProContent>
          <ul class="grid w-[420px] grid-cols-2 gap-1">
            <li>
              <NavigationMenuProLink href="#">
                <div class="flex items-center gap-2 text-sm font-medium text-foreground">
                  <ClipboardListIcon aria-hidden="true" /><span>工单</span>
                </div>
                <div class="mt-0.5 pl-6 text-xs text-muted-foreground">下达、跟踪与报工 WO-2406</div>
              </NavigationMenuProLink>
            </li>
            <li>
              <NavigationMenuProLink href="#">
                <div class="flex items-center gap-2 text-sm font-medium text-foreground">
                  <CalendarRangeIcon aria-hidden="true" /><span>排产</span>
                </div>
                <div class="mt-0.5 pl-6 text-xs text-muted-foreground">甘特排程与产能平衡</div>
              </NavigationMenuProLink>
            </li>
            <li>
              <NavigationMenuProLink href="#">
                <div class="flex items-center gap-2 text-sm font-medium text-foreground">
                  <BarChart3Icon aria-hidden="true" /><span>报表</span>
                </div>
                <div class="mt-0.5 pl-6 text-xs text-muted-foreground">产量、节拍与达成率</div>
              </NavigationMenuProLink>
            </li>
            <li>
              <NavigationMenuProLink href="#">
                <div class="flex items-center gap-2 text-sm font-medium text-foreground">
                  <ActivityIcon aria-hidden="true" /><span>实时看板</span>
                </div>
                <div class="mt-0.5 pl-6 text-xs text-muted-foreground">产线 L01–L12 在线状态</div>
              </NavigationMenuProLink>
            </li>
          </ul>
        </NavigationMenuProContent>
      </NavigationMenuProItem>
      <NavigationMenuProItem>
        <NavigationMenuProTrigger>设备</NavigationMenuProTrigger>
        <NavigationMenuProContent>
          <ul class="grid w-[320px] gap-1">
            <li>
              <NavigationMenuProLink href="#">
                <div class="flex items-center gap-2 text-sm font-medium text-foreground">
                  <GaugeIcon aria-hidden="true" /><span>OEE 监控</span>
                </div>
                <div class="mt-0.5 pl-6 text-xs text-muted-foreground">可用率 / 性能 / 良率</div>
              </NavigationMenuProLink>
            </li>
            <li>
              <NavigationMenuProLink href="#">
                <div class="flex items-center gap-2 text-sm font-medium text-foreground">
                  <WrenchIcon aria-hidden="true" /><span>点检保养</span>
                </div>
                <div class="mt-0.5 pl-6 text-xs text-muted-foreground">CNC-07 今晚 22:00 窗口</div>
              </NavigationMenuProLink>
            </li>
          </ul>
        </NavigationMenuProContent>
      </NavigationMenuProItem>
      <NavigationMenuProItem>
        <NavigationMenuProTrigger>质量</NavigationMenuProTrigger>
        <NavigationMenuProContent>
          <ul class="grid w-[320px] gap-1">
            <li>
              <NavigationMenuProLink href="#">
                <div class="flex items-center gap-2 text-sm font-medium text-foreground">
                  <ShieldCheckIcon aria-hidden="true" /><span>在线判定</span>
                </div>
                <div class="mt-0.5 pl-6 text-xs text-muted-foreground">当班良率 99.2%</div>
              </NavigationMenuProLink>
            </li>
            <li>
              <NavigationMenuProLink href="#">
                <div class="flex items-center gap-2 text-sm font-medium text-foreground">
                  <ScanLineIcon aria-hidden="true" /><span>追溯</span>
                </div>
                <div class="mt-0.5 pl-6 text-xs text-muted-foreground">批次 / 序列号正反向追溯</div>
              </NavigationMenuProLink>
            </li>
          </ul>
        </NavigationMenuProContent>
      </NavigationMenuProItem>
      <NavigationMenuProItem>
        <NavigationMenuProLink bar href="#">
          <span class="flex items-center gap-1.5">
            <SettingsIcon aria-hidden="true" /><span>设置</span>
          </span>
        </NavigationMenuProLink>
      </NavigationMenuProItem>
      <NavigationMenuProIndicator />
    </NavigationMenuProList>
  </NavigationMenuPro>
</Demo>

```vue
<NavigationMenuPro>
  <NavigationMenuProList>
    <NavigationMenuProItem>
      <NavigationMenuProTrigger>生产</NavigationMenuProTrigger>
      <NavigationMenuProContent>
        <ul class="grid w-[420px] grid-cols-2 gap-1">
          <li>
            <NavigationMenuProLink href="/work-orders">
              <div class="text-sm font-medium text-foreground">工单</div>
              <div class="text-xs text-muted-foreground">下达、跟踪与报工 WO-2406</div>
            </NavigationMenuProLink>
          </li>
          <!-- 排产 / 报表 / 实时看板… -->
        </ul>
      </NavigationMenuProContent>
    </NavigationMenuProItem>

    <!-- 直达链接：不展开面板 -->
    <NavigationMenuProItem>
      <NavigationMenuProLink bar href="/settings">设置</NavigationMenuProLink>
    </NavigationMenuProItem>

    <NavigationMenuProIndicator />
  </NavigationMenuProList>
</NavigationMenuPro>
```

## 组成

- `NavigationMenuPro` — 根容器，默认在导航条下方渲染内置动画视口（`viewport`，可关）。
- `NavigationMenuProList` / `NavigationMenuProItem` — 横向列表与单项。
- `NavigationMenuProTrigger` — 展开面板的按钮，带随展开旋转 180° 的箭头（`chevron`，可关）。
- `NavigationMenuProContent` — 面板内容，被 reka 提升进视口；按移动方向左右滑入。
- `NavigationMenuProLink` — 链接；`bar` 时为顶部条目样式，否则为面板内富链接。
- `NavigationMenuProIndicator` — 滑向激活触发器、指向面板的小菱形指示器。
- `NavigationMenuProViewport` — 玻璃面板容器，随内容动画改变尺寸并脱离裁剪（一般无需手动放置）。

## 属性

| 组件 | 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|---|
| `NavigationMenuPro` | `model-value` | 受控当前展开项（`v-model`） | `string` | — |
| `NavigationMenuPro` | `default-value` | 默认展开项（非受控） | `string` | — |
| `NavigationMenuPro` | `delay-duration` | 悬停展开延迟（毫秒） | `number` | `200` |
| `NavigationMenuPro` | `viewport` | 是否渲染内置动画视口 | `boolean` | `true` |
| `NavigationMenuProTrigger` | `chevron` | 是否显示旋转箭头 | `boolean` | `true` |
| `NavigationMenuProLink` | `bar` | 作为顶部条目样式渲染 | `boolean` | `false` |
| `NavigationMenuProLink` | `href` | 链接地址 | `string` | — |
| `NavigationMenuProLink` | `active` | 是否为当前激活链接 | `boolean` | `false` |
