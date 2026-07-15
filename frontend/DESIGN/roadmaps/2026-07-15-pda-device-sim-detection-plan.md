# PDA 真机模拟检测方案（补全 L2 真实栈仿真层 + L3 模拟器层）

> 状态：提案（待 owner 裁决分期）。日期：2026-07-15。
> 事实依据：`docs/architecture/mobile-pda-testing-and-smoke.md`（现行三层口径）、
> `docs/architecture/mobile-pda-deployment.md`（APK 再生与网关基址）、
> `frontend/packages/ui-mobile/src/components/scan-bar/ScanBar.vue`（键盘楔入焦点契约）、
> `frontend/apps/business-pda/playwright.config.ts` + `e2e/`（现有全 mock e2e）、
> `frontend/DESIGN/roadmaps/2026-07-14-man457-scan-e2e-walkthrough.md`（真实栈走查先例）。

## 1. 问题

PDA 域「真机」的既定口径（MAN-457 owner 裁决）= **目标 PDA 硬件 + APK + 实体扫码枪**；
浏览器 Playwright 只能算 e2e。但实体硬件长期不可得，导致近期 PDA PR 连续以
「真机走查未做」交付（MAN-456 复验受阻、MAN-458 仅门禁、MAN-459 Docker 停 + 无 seed）。

现状测试设施三层：

1. **L0 jsdom 单元/组件测试** —— 快，但测不到布局/焦点/导航。
2. **L1 Playwright e2e（全 mock）** —— `page.route` mock 网关、`seedStoredSession` 注入会话，
   移动视口 390×844，无需后端。
3. **L4 真机手动冒烟清单** —— 需要实体 PDA + APK + 扫码枪，长期无法执行。

**断层在 L1 与 L4 之间**：既没有「真实后端 + 高保真设备仿真」的可重复层，也没有
「Capacitor 原生路径」的任何验证手段。缺硬件是常态而非例外，方案目标是：
**没有硬件时也能把真机差异点逐项仿真掉，把 L4 收缩到真正不可仿真的最小集合**。

## 2. 差异点清单：到底要仿真什么

| #   | 真机差异点                                                      | L1 现覆盖           | 可仿真手段                                         | 剩余真机项（L4）    |
| --- | --------------------------------------------------------------- | ------------------- | -------------------------------------------------- | ------------------- |
| 1   | 扫码枪键盘楔入：突发字符流（无触摸、~10–30ms 间隔）+ Enter 后缀 | `fill()+Enter` 近似 | `page.keyboard` 突发注入（L2）；`adb input`（L3）  | 电气时序/按键重复率 |
| 2   | ScanBar 焦点常驻 / 失焦回抢 / 浮层 `active=false` opt-out       | 未覆盖              | 可完全仿真（L2，见 §4.1）                          | —                   |
| 3   | Capacitor 原生路径：`isNativePlatform`、APK 内无 dev proxy      | 不可                | Android 模拟器 + APK（L3）                         | 厂商 ROM 差异       |
| 4   | 真实 `env(safe-area-inset-*)`（刘海/手势条）                    | 仅 fallback 最小值  | AVD 挖孔屏镜像（L3）                               | 真机刘海实测        |
| 5   | 弱网 / 断网 / 30s 超时（MAN-460 传输层）                        | 未覆盖              | `context.setOffline` + CDP 节流 + route 延迟（L2） | 真实蜂窝/漫游       |
| 6   | 真实后端数据与写路径（非 mock）                                 | 全 mock             | L2 真实栈走查（man457 先例工程化）                 | —                   |
| 7   | WebView 内核 vs 桌面 Chromium                                   | 不可                | AVD 系统 WebView（L3）                             | 厂商 ROM WebView    |
| 8   | 相机能力（拍照采集，能力门控两个分支）                          | 不可                | fake media stream（L2）；模拟器虚拟相机（L3）      | 真机摄像头          |
| 9   | 触感/单手可达/扫码枪握持姿势                                    | 不可                | 不可                                               | 保留                |

## 3. 分层方案总览

| 层  | 名称                             | 状态 | 触发时机                                        |
| --- | -------------------------------- | ---- | ----------------------------------------------- |
| L0  | jsdom 单元/组件                  | 已有 | 每 PR（CI 门禁）                                |
| L1  | Playwright e2e（全 mock）        | 已有 | 每 PR（本地必跑；浏览器不可用按既有降级口径）   |
| L2  | **真实栈仿真走查（新增）**       | 新增 | 每个 PDA 业务 PR（本地栈可用时；核心交付）      |
| L3  | **Android 模拟器 + APK（新增）** | 新增 | 发版前必跑；触及 Capacitor/焦点/safe-area 的 PR |
| L4  | 实体真机冒烟（收缩）             | 保留 | 发版勾验（范围收缩到 §6）                       |

L2 依赖本地完整栈，**不进 CI 门禁**（与 `*PostgresProfileTests` 环境门控同一口径）；
L3 首期本地执行，CI 化后置。

## 4. L2：真实栈仿真走查 harness（核心交付）

把 man457 那次一次性的真实栈走查工程化为可重复资产。

**形态**：`frontend/apps/business-pda/e2e-live/` 独立目录 + Playwright 第二 project `live`
（`PLAYWRIGHT_PDA_LIVE=1` 才收集，默认不跑、不进 CI）。与 `e2e/` 的区别：

- **无 `page.route` mock**：vite dev（proxy → BusinessGateway 5119）+ 真实 IAM admin 登录。
- 设备仿真沿用 Pixel 5 + 390×844 + touch。

### 4.1 扫码枪信号仿真器 `simulateScanGun()`

这是 owner 在 MAN-457 提过的「后期模拟信号检测」的落地。基于 `ScanBar.vue` 的行为契约
（焦点常驻、`@blur` 后 `requestAnimationFrame` 回抢、浮层场景 `active=false` 停止回抢、
`inputmode="none"` 不弹系统键盘）：

```ts
// e2e-live/support/scan-gun.ts
interface ScanGunProfile {
  interCharDelayMs: number // 默认 15，模拟楔入突发
  suffix: 'Enter' | 'Tab'
  prefix?: string
}
async function simulateScanGun(page: Page, code: string, profile?: Partial<ScanGunProfile>)
```

关键：**不 `focus()`、不 `fill()`**，直接 `page.keyboard` 按 profile 时序打字符流——值必须
经由「焦点常驻」的前提进入 ScanBar，这才是楔入设备的真实路径。三个固定场景：

- **S1 常驻直扫**：页面加载后不做任何点击，直接扫 → 值进入 + `scan` 事件命中。
- **S2 失焦回抢竞态**：点击页面空白区使 ScanBar blur → **立即**（不等待）扫码 →
  断言首字符不丢。回抢走 RAF，blur 与回抢之间有一帧窗口，这是已知产品风险点，
  只有突发仿真能暴露，`fill()` 永远测不出。
- **S3 浮层不抢焦**：打开 BottomSheet/Dialog（消费方应传 `active=false`）→ 扫码 →
  断言焦点仍在浮层内、ScanBar 不回抢不吞键。逐页扫消费方是否漏传 `active`。

同一 helper 附带浏览器手动走查用法（console 粘贴片段）；可选加一个
`import.meta.env.DEV` 门控的悬浮「模拟扫码」按钮（M2，供人工走查点按注码）。

### 4.2 网络/超时仿真（覆盖真机清单第 5 条的可自动化部分）

- `context.setOffline(true)` → 验 MAN-460 离线预检的类型化错误文案逐页透出。
- CDP `Network.emulateNetworkConditions` 慢网 → loading 态不闪断、不重复提交（幂等键稳定）。
- `page.route` 挂起 >30s → 验 AbortController 超时路径文案（区别于导航取消不误报）。

### 4.3 相机能力门控双分支

- 有相机分支：launch args `--use-fake-ui-for-media-stream --use-fake-device-for-media-stream`
  点亮 `mediaDevices` → 拍照入口渲染、本地缩略图出现（不上送，与 #924 缺口口径一致）。
- 无相机分支：默认 context 无 fake 设备 → 断言拍照入口整体隐藏（能力门控反向）。
- `Capacitor.isNativePlatform === true` 分支 L2 点不亮，归 L3。

### 4.4 数据前置

- **优先真实 seed**：`QualitySeedService` 先例推广，各域缺口逐个补 seed service 或
  内部造数端点（报警域 5116 POST 先例）。
- **兜底 `setQueryData` 缓存注入**（man457 已验证配方）：仅用于读面 UI 验证，
  **不得作为写路径证据**，走查记录中必须标注注入而非真数据。

### 4.5 运行脚本与证据口径

- `frontend/apps/business-pda/scripts/pda-live-walkthrough.ps1`（pwsh）串起：
  worktree 归属检查（不误测并行会话的栈）→ 起/复用栈（`nerv.ps1 dev`）→ seed →
  跑 `live` spec → Playwright 截图落 `DESIGN/roadmaps/assets/<date>-<topic>/`。
- 截图一律走 Playwright（preview MCP 截图已知 30s 超时）。
- 走查记录沿用 man457 模板：环境表 + 步骤/断言/截图表 + 「已覆盖 / 未覆盖（留待 L3/L4）」
  声明，杜绝层级虚报。

## 5. L3：Android 模拟器 + APK 层

- **构建**：按 `mobile-pda-deployment.md` 口径 `cap add android` 确定性再生 →
  `.env` 设**绝对**网关基址（模拟器内宿主机为 `http://10.0.2.2:5119`，APK 内无 dev proxy）→
  `gradlew assembleDebug`。前提 JDK 17 + Android SDK。
- **AVD 选型**：带挖孔/手势条的系统镜像 → 真实 `env(safe-area-inset-*)` 非零值可断言
  （L1 只能断 fallback 最小值）；支持 `emulator -no-window` 无头运行。
- **扫码仿真**：`adb shell input text '<code>' && adb shell input keyevent 66`——OS 级
  键盘注入，比浏览器 `page.keyboard` 更接近楔入设备（经过 IME/焦点系统）。若未来接
  Zebra DataWedge，则改 `adb shell am broadcast` intent 仿真，helper 留扩展位。
- **相机**：模拟器虚拟相机（`webcam0` 或虚拟场景）→ `Capacitor.isNativePlatform === true`
  分支真点亮，验原生拍照路径。
- **断言与存证**：首期「脚本化构建安装 + 人工按 L4 清单勾验 + `adb exec-out screencap` 存证」；
  自动化（Playwright experimental Android 经 adb 驱动 WebView，可复用 spec 代码）标记
  **实验性**，视 M3 效果决定是否做 M4。
- **触发**：发版前必跑；触及 Capacitor 配置、网关基址、扫码焦点逻辑、safe-area 的 PR 必跑。

## 6. L4 真机清单收缩

L2/L3 落地后，现行 5 条清单收缩为发版勾验 3 项（其余由 L2/L3 前挡）：

1. 实体扫码枪实扫：电气时序/按键重复率/连扫节奏（唯一完全不可仿真的核心项）。
2. 真机刘海/手势条实际 inset 不压内容（L3 AVD 先挡，真机确认）。
3. 目标 PDA 厂商 ROM WebView 一屏冒烟（渲染/字体/滚动惯性）。

**口径不变**：L2/L3 是「真机模拟检测」，不改写「真机」定义；发版门仍是 L4。

## 7. PR 交付达标口径（修订建议）

| PR 类型                                          | 最低要求                                                                                  |
| ------------------------------------------------ | ----------------------------------------------------------------------------------------- |
| 纯 PDA 前端业务 PR                               | L0/L1 门禁 + **L2 走查脚本跑绿 + 截图**；栈不可用时如实标注受阻原因（不伪造、不降级虚报） |
| 触及 Capacitor / 扫码焦点 / safe-area / 网关基址 | 追加 L3                                                                                   |
| 发版                                             | L3 全量 + L4 三项勾验                                                                     |

PR body 固定声明格式：`走查层级：L2（真实栈仿真）｜证据：<走查记录链接>｜未覆盖：<留待 L3/L4 项>`。

## 8. 分期交付

- **M1（1 PR，核心）**：`simulateScanGun` helper + `live` project 骨架 + quality 链路 1 条
  live spec（S1–S3 全场景）+ `pda-live-walkthrough.ps1` + 本方案口径并入
  `mobile-pda-testing-and-smoke.md`。
- **M2（1 PR）**：弱网/断网/超时 spec + 相机双分支 spec +（可选）dev 悬浮模拟扫码件。
- **M3（1–2 PR）**：Android 再生/构建脚本 + AVD 指南（挖孔镜像 + 10.0.2.2 基址）+
  adb 扫码/截图脚本 + 发版 checklist 更新。
- **M4（视 M3 效果）**：Playwright experimental Android 自动化 L3。

**风险**：L2 依赖本地栈与 seed（不进 CI，同 Postgres 集成测试口径）；AVD 对 CI 太重（先本地）；
Playwright Android 属实验 API（故 M4 独立、可弃）。

## 9. 明确不做（仍属 L4 或超范围）

实体扫码枪电气时序与按键重复率、厂商 ROM WebView、真机触感/握持/单手可达、
真实刘海设备差异、iOS（当前无目标设备）。
