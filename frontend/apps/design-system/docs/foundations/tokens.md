# 设计令牌

所有视觉常量都收敛为**语义令牌**，唯一真源是 `@nerv-iip/ui` 的 `src/styles/theme.css`（Tailwind v4 `@theme inline` + OKLCH）；大屏另有独立的场景令牌表 `components/screen/tokens.css`。组件只引用语义名（如 `--nv-brand`、`--muted-foreground`），不写死颜色——这样动态色、亮暗切换、跨表面复用都能一处生效。

本页分两部分：**统一的三层令牌哲学**（所有表面共享的"为什么"），以及**四场景参数矩阵**（颜色 / 圆角 / 间距 / 字号 / 触控尺寸 / 动效 逐项的"场景 × 值"）。

## 三层令牌体系（OKLCH）

令牌分三层，从原始值到组件专用，逐层收敛：

| 层                     | 例子                                                      | 谁引用它                                            |
| ---------------------- | --------------------------------------------------------- | --------------------------------------------------- |
| **1 · Primitive 原始** | `oklch(0.54 0.16 256)`、`cubic-bezier(0.25,1,0.5,1)`      | 只在 `theme.css` 定义一次，**组件模板永不直接引用** |
| **2 · Semantic 语义**  | `--nv-brand`、`--muted-foreground`、`--nv-ease-out-quart` | 组件、页面、场景都引用这一层                        |
| **3 · Component 组件** | Badge / Button 变体（CVA 内部）                           | 由 shadcn-vue / Nv 组件内部消费，一般不外露         |

### 为什么用 OKLCH

OKLCH 在感知上更均匀——同一亮度下切换色相（动态色 12 板）能保持一致的"品牌强度"，不会某些颜色突然显得过亮或过脏。动态色轮就是一圈**等亮度**色相，见[色彩与动态色](/foundations/color)。

## 场景命名空间（ADR 0020 §3）

token 名称按场景命名空间隔离，看名字即知归属。规则见 [ADR 0020](https://github.com/Mang-X/Nerv-IIP/blob/main/docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md)，已落地 MAN-436 / #790。

| 命名空间               | 场景            | 现状                                                                                                                                               |
| ---------------------- | --------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| 契约层（无前缀，冻结） | shadcn 原版依赖 | `--background` `--primary` `--border` `--chart-*` `--sidebar-*` `--radius`——改名等于改原版，永不加前缀                                             |
| `--nv-*`               | PC / 共享语义   | 项目自有扩展：`--nv-brand` `--nv-success` `--nv-warning` `--nv-*-strong` `--nv-ease-*` `--nv-duration-*` `--nv-shadow-*`（PC / 移动 / 一体机共用） |
| `--nv-scr-*`           | 大屏 screen     | 独立工业蓝全表（30 项，下方逐项列出），近黑深蓝、固定深色，不随动态色/亮暗切换                                                                     |
| `--nv-m-*`             | 移动 mobile     | **当前空集，规范先行**——移动 token 现全部来自共享 `--nv-*`；出现首个移动专属值时建立                                                               |
| `--nv-t-*`             | 一体机 touch    | **当前空集，规范先行**——一体机沿用共享 `--nv-*`，差异体现在组件尺寸类而非独立色板                                                                  |

规则：**primitive 值全库共享**；**允许跨场景取值相同，但名称必须隔离**——同值用 var 引用链表达（`--nv-scr-ease: var(--nv-ease-out-quart)`），禁止复制字面量；场景组件只允许引用本场景前缀 + 契约层 token（contract test 拦截跨场景直引）。

> 一个迁移周期内旧名（`--brand` / `--success` / `--sb-*`）仍以 var 链别名保留，下一周期（收口批）删除。

## 语义颜色（实时取色，跟随当前主题）

<div class="grid grid-cols-2 sm:grid-cols-4 gap-3 my-6">
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--background)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--background</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--card)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--card</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--muted)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--muted</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--nv-brand)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--nv-brand</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--nv-success)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--nv-success</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--nv-warning)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--nv-warning</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--destructive)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--destructive</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--foreground)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--foreground</div>
  </div>
</div>

> 切换右上角的外观（亮/暗）与动态色，上面的色块会实时跟随——证明组件只依赖语义令牌。

---

# 四场景参数矩阵

同一套设计语言，四种原生尺度。**颜色与动效的取值** PC / 移动 / 一体机三者共享（大屏独立）；真正逐场景不同的是**触控尺寸、字号、间距、圆角**——它们由表面的视距与输入方式决定。下表逐项给"场景 × 值"。

## 颜色

| 角色        | PC / 移动 / 一体机（共享 `--nv-*` + 契约层）                                            | 大屏 `--nv-scr-*`（独立工业蓝，固定深色）                              |
| ----------- | --------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| 品牌 / 强调 | `--nv-brand` `oklch(0.54 0.16 256)`（暗 `0.64…`）· 运行时动态色                         | `--nv-scr-cyan` `#4aa6ee`（唯一动作/数据蓝，固定）                     |
| 成功        | `--nv-success` `oklch(0.6 0.12 160)`                                                    | `--nv-scr-green` `#45d089`                                             |
| 警告        | `--nv-warning` `oklch(0.72 0.13 68)`                                                    | `--nv-scr-amber` `#f2c14e`                                             |
| 危险        | `--destructive` `oklch(0.55 0.2 25)`（契约名）                                          | `--nv-scr-red` `#ef5a63`                                               |
| 底 / 面     | `--background` `oklch(0.985 0 0)` ≠ `--card` `oklch(1 0 0)`（亮）；暗 `0.145` / `0.205` | `--nv-scr-bg` `#03050b` · 半透明面板 `--nv-scr-panel-a/b`              |
| 文字        | `--foreground` / `--muted-foreground` / `--*-strong`（小字着色 ≥4.5:1）                 | `--nv-scr-text` `#f2f5fa` / `-2` / `-muted` / `-faint`（远读向白收拢） |
| 边线        | `--border` `oklch(0.922 0 0)`                                                           | `--nv-scr-line` `rgba(255,255,255,.06)` + 发丝线高光                   |

- **动态色 & 亮暗**：PC / 移动 / 一体机的品牌色是运行时可切换的动态色，且亮暗双态；大屏 `--nv-scr-*` 是固定的近黑工业蓝，**不参与动态色与亮暗切换**（控制室常暗）。
- **强调文字**用 `--nv-*-strong` 变体（比填充色更深/更浅），保证 10% 淡底上的小字过 WCAG AA。

### 大屏 `--nv-scr-*` 全表（30 项）

大屏是唯一拥有独立场景令牌层的表面——一套**固定**的近黑工业蓝，不随动态色/亮暗切换。真源是 `@nerv-iip/ui` 的 `components/screen/tokens.css`；动效名 var 链指向共享 Nv 曲线（值只在 `theme.css` 定义一次）。

| 分组 | 令牌                       | 值                                                              | 用途                           |
| ---- | -------------------------- | --------------------------------------------------------------- | ------------------------------ |
| 表面 | `--nv-scr-bg`              | `#03050b`                                                       | 舞台底                         |
| 表面 | `--nv-scr-bg-accent`       | `#081020`                                                       | 次级底                         |
| 表面 | `--nv-scr-panel-a`         | `rgba(22,34,60,.5)`                                             | 面板渐变起（半透，透出底纹）   |
| 表面 | `--nv-scr-panel-b`         | `rgba(10,16,30,.38)`                                            | 面板渐变止                     |
| 表面 | `--nv-scr-line`            | `rgba(255,255,255,.06)`                                         | 发丝分隔线                     |
| 表面 | `--nv-scr-line-2`          | `rgba(255,255,255,.1)`                                          | 略强分隔线                     |
| 表面 | `--nv-scr-divider`         | `rgba(255,255,255,.055)`                                        | 分割线                         |
| 强调 | `--nv-scr-cyan`            | `#4aa6ee`                                                       | 唯一动作 / 数据蓝              |
| 强调 | `--nv-scr-cyan-dim`        | `rgba(74,166,238,.45)`                                          | 弱化青                         |
| 强调 | `--nv-scr-accent-from`     | `#4aa6ee`                                                       | "选中填充"渐变起               |
| 强调 | `--nv-scr-accent-to`       | `#2a72cc`                                                       | "选中填充"渐变止               |
| 强调 | `--nv-scr-accent-fill`     | `linear-gradient(180deg,#4aa6ee,#2a72cc)`                       | 统一选中填充（按钮/分段/开关） |
| 强调 | `--nv-scr-accent-edge`     | `rgba(90,165,240,.5)`                                           | 选中描边                       |
| 强调 | `--nv-scr-indigo`          | `#8b9be6`                                                       | 次强调靛                       |
| 强调 | `--nv-scr-green`           | `#45d089`                                                       | 成功 / 良好                    |
| 强调 | `--nv-scr-amber`           | `#f2c14e`                                                       | 警告                           |
| 强调 | `--nv-scr-red`             | `#ef5a63`                                                       | 危险 / 告警                    |
| 文字 | `--nv-scr-text`            | `#f2f5fa`                                                       | 主文字（近白，远读向白收拢）   |
| 文字 | `--nv-scr-text-2`          | `#e2e9f2`                                                       | 次文字                         |
| 文字 | `--nv-scr-muted`           | `#c3cdda`                                                       | 弱文字                         |
| 文字 | `--nv-scr-faint`           | `#98a3b3`                                                       | 最弱文字                       |
| 效果 | `--nv-scr-highlight`       | `rgba(255,255,255,.09)`                                         | 顶部高光发丝                   |
| 效果 | `--nv-scr-edge-gradient`   | `linear-gradient(90deg, rgba(135,208,255,.28) → .08 → .18)`     | 面板边框渐变（两侧略亮）       |
| 效果 | `--nv-scr-value-glow`      | `0 0 12px rgba(150,190,235,.18)`                                | 大数值白蓝辉光                 |
| 效果 | `--nv-scr-edge-glow`       | `rgba(95,170,230,.3)`                                           | 边角微辉光                     |
| 效果 | `--nv-scr-glow`            | `0 0 8px rgba(95,170,230,.28)`                                  | 通用微辉光                     |
| 效果 | `--nv-scr-sheen`           | `inset 0 1px 0 rgba(255,255,255,.06), 0 1px 3px rgba(0,0,0,.5)` | 控件高光 + 投影                |
| 尺寸 | `--nv-scr-radius`          | `8px`                                                           | 面板 / 控件圆角                |
| 动效 | `--nv-scr-ease`            | `var(--nv-ease-out-quart)`                                      | 通用缓动（引用共享）           |
| 动效 | `--nv-scr-ease-emphasized` | `var(--nv-ease-out-expo)`                                       | 强调缓动（引用共享）           |

## 圆角

| 场景       | 令牌 / 类                           | 值                                          |
| ---------- | ----------------------------------- | ------------------------------------------- |
| PC（共享） | `--radius`                          | `8px`（`sm 4` / `md 6` / `lg 8` / `xl 12`） |
| 移动       | 共享 `--radius`                     | 同 PC；卡片/抽屉常用 `xl 12`–`16`           |
| 一体机     | 组件类 `rounded-xl` / `rounded-2xl` | `12px` / `16px`（大触控更圆润）             |
| 大屏       | `--nv-scr-radius`                   | `8px`（面板统一）                           |

## 间距

间距沿用 Tailwind 4px 基准步进（`gap-1` = 4px…），**不设独立令牌**；逐场景差异体现在组件的内边距与间距密度：

| 场景   | 典型控件内边距                | 典型区块间距                                 | 密度             |
| ------ | ----------------------------- | -------------------------------------------- | ---------------- |
| PC     | `px-3`–`px-4`（12–16px）      | `gap-2`–`gap-4`（8–16px）                    | 高——信息密度优先 |
| 移动   | `px-4`（16px）                | `gap-2`–`gap-3`，段落 `p-4`                  | 中——单手可点     |
| 一体机 | `px-5`–`px-6`（20–24px）      | `gap-4`–`gap-5`（16–20px），卡片 `p-5`–`p-6` | 低——大触控留白   |
| 大屏   | 面板 `padding` 大、模块间距大 | 舞台缩放后按分辨率放大                       | 低——远读留白     |

## 字号

正文与读数按视距放大；均走 Tailwind `text-*` 与 `--font-sans`（Inter + 系统中文），大屏另有巨号翻牌数字。

| 场景   | 正文                              | 主数值 / 读数                              | 说明               |
| ------ | --------------------------------- | ------------------------------------------ | ------------------ |
| PC     | `text-sm` 14px                    | `text-2xl`–`text-3xl`                      | 键鼠近读，密度优先 |
| 移动   | `text-[15px]`–`text-base` 15–16px | `text-xl`–`text-2xl`                       | 手持中距，防误读   |
| 一体机 | `text-base` 16px                  | `text-2xl`–`text-4xl`（报工/指标）         | 站立近距大触控     |
| 大屏   | `≥14px`（标签下限）               | 翻牌/核心指标巨号（DigitalFlop / OeeHero） | 挂墙远读，主次分明 |

> 大屏红线：任何标签 **≥14px**，KPI 要有 hero 主次（见[大屏概览](/components/screen/)）。

## 触控尺寸

**四场景最本质的差异**：触控目标随视距与输入方式逐级放大。数值取自各表面主按钮的真实高度。

| 场景   | 触控目标                | 主按钮高度（组件实测）                                                        | 输入方式             |
| ------ | ----------------------- | ----------------------------------------------------------------------------- | -------------------- |
| PC     | **36–40px** 紧凑        | [NvButton](/components/desktop/button) `sm 32` / 默认 `36` / `lg 40`          | 指针（键鼠）         |
| 移动   | **40–48px**（≥44 基线） | [NvMobileButton](/components/mobile/button) `sm 32` / `md 40` / `lg 48`       | 原生触控（拇指）     |
| 一体机 | **56–72px** 大触控      | [NvTouchButton](/components/touch/touch-button) `md 44` / `lg 56` / `xl 72`   | 站立触控（可戴手套） |
| 大屏   | 远读为主，几乎不触控    | 少量控件（[NvScreenButton](/components/screen/screen-button) 等）按大屏化放大 | 远观 / 偶发遥控      |

## 动效

曲线与时长的**唯一定义**在共享 Nv 语义层，四场景引用同一套词汇；大屏用引用链改名而非复制字面量。完整表与手感（橡皮筋阻尼）见[动效](/foundations/motion)。

| 用途            | 令牌（共享定义）            | 值                              |
| --------------- | --------------------------- | ------------------------------- |
| 通用进入 / 悬停 | `--nv-ease-out-quart`       | `cubic-bezier(0.25, 1, 0.5, 1)` |
| 滑动吸附 / 抽屉 | `--nv-ease-out-expo`        | `cubic-bezier(0.16, 1, 0.3, 1)` |
| 功能型弹层进入  | `--nv-duration-fast-invoke` | `187ms`                         |
| 普通微交互      | `--nv-duration-fast`        | `150ms`                         |

| 场景               | 引用方式                                                                                                                                                      |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| PC / 移动 / 一体机 | 直接引用共享 `--nv-ease-*` / `--nv-duration-*`；移动手势另加统一橡皮筋阻尼曲线                                                                                |
| 大屏               | 场景引用名 var 链指向共享值：`--nv-scr-ease: var(--nv-ease-out-quart)`、`--nv-scr-ease-emphasized: var(--nv-ease-out-expo)`（**不复制** cubic-bezier 字面量） |

- JS 侧动效统一走 motion-v 封装，预设唯一来源 `packages/ui/src/lib/motion.ts`（`nervMotion`），数值与 CSS token 同表对齐。
- 所有位移类动画带 `prefers-reduced-motion` 降级。

## 令牌族速查

| 族   | 示例                                                            | 说明                                    |
| ---- | --------------------------------------------------------------- | --------------------------------------- |
| 表面 | `--background` `--card` `--popover` `--muted`                   | 从底到浮层的层级                        |
| 文字 | `--foreground` `--muted-foreground` `--*-strong`                | 含高对比强调变体                        |
| 品牌 | `--nv-brand` `--nv-brand-strong` `--nv-brand-foreground`        | 运行时动态色                            |
| 语义 | `--destructive` `--border` `--ring` `--accent`                  | 状态与边线                              |
| 动效 | `--nv-ease-out-quart / expo / in-out-quart`                     | 统一缓动，见[动效](/foundations/motion) |
| 大屏 | `--nv-scr-cyan` `--nv-scr-bg` `--nv-scr-text` `--nv-scr-radius` | 独立工业蓝层（30 项）                   |

## 新增令牌

所有 token 编辑都发生在共享 `packages/ui/src/styles/theme.css` 或所属场景表（大屏：`components/screen/tokens.css`）——绝不写进 app 的 `main.css`。流程：先按场景命名空间定前缀（契约层冻结 / `--nv-*` / `--nv-scr-*` / `--nv-m-*` / `--nv-t-*`）；跨场景同值用 var 引用链；在 `:root` 定义并补 `.dark`；在 `@theme inline` 加 Tailwind 桥（桥名保持 utility 契约，右值指向命名空间新名）；更新本页；设计关键令牌加 `design-system.contract.test.ts` 断言。
