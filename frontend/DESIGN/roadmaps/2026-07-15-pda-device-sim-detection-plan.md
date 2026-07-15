# PDA 真机模拟检测方案（补全 L2 真实栈仿真层 + L3 模拟器层）

> 状态：提案 v2（待 owner 裁决分期）。日期：2026-07-15。
> v2 修订：经 codex（gpt-5.6-sol，reasoning effort=high）对抗式评审（裁决「需修订后落地」），
> 全部 P0 意见经本地代码核实属实并已吸收，见 §10。
> 事实依据：`docs/architecture/mobile-pda-testing-and-smoke.md`（现行分层口径，注意其 spec 计数已漂移）、
> `docs/architecture/mobile-pda-deployment.md`（APK 再生与网关基址）、
> `frontend/packages/ui-mobile/src/components/scan-bar/ScanBar.vue`（键盘楔入焦点契约）、
> `frontend/apps/business-pda/vite.config.ts`（双网关代理拓扑）、
> `frontend/apps/business-pda/playwright.config.ts` + `e2e/`（现有全 mock e2e）、
> `frontend/DESIGN/roadmaps/2026-07-14-man457-scan-e2e-walkthrough.md`（真实栈走查先例）。

## 1. 问题

PDA 域「真机」的既定口径（MAN-457 owner 裁决）= **目标 PDA 硬件 + APK + 实体扫码枪**；
浏览器 Playwright 只能算 e2e。但实体硬件长期不可得，导致近期 PDA PR 连续以
「真机走查未做」交付（MAN-456 复验受阻、MAN-458 仅门禁、MAN-459 Docker 停 + 无 seed）。

现状测试设施三层：

1. **L0 jsdom 单元/组件测试** —— 快，但测不到布局/焦点/导航。
2. **L1 Playwright e2e（全 mock）** —— `page.route` mock 网关、`seedStoredSession` 注入会话，
   移动视口 390×844，无需后端。实际规模为 **5 个 spec / 25 个用例**（app-flow、ui-mobile、
   wms、mes、equipment）；`mobile-pda-testing-and-smoke.md` 仍写「2 spec / 9 用例」，
   该文档需随本方案 M1 一并更新。
3. **L4 真机手动冒烟清单** —— 需要实体 PDA + APK + 扫码枪，长期无法执行。

**断层在 L1 与 L4 之间**：既没有「真实后端 + 高保真设备仿真」的可重复层，也没有
「Capacitor 原生路径」的任何验证手段。缺硬件是常态而非例外，方案目标是：
**没有硬件时也能把真机差异点逐项仿真掉，为最终收缩 L4 积累对照证据**。

## 2. 差异点清单：到底要仿真什么

**保真等级声明**：下表「可仿真手段」分四级——DOM 近似（桌面 Chromium 键盘事件）、
Android 输入栈注入（adb，经 IME/焦点系统）、Android WebView（AVD 内真实渲染）、
实体硬件（HID 设备 + 厂商扫码服务）。L2/L3 均**不是硬件等价**，只是逐级逼近；
`page.keyboard` 与 `adb input` 都仿真不了 USB HID scan code、KeyCharacterMap、
厂商扫码服务（如 Zebra DataWedge）、键重复/丢键与键盘布局差异。

| #   | 真机差异点                                                      | L1 现覆盖                                                             | 可仿真手段                                                              | 剩余真机项（L4）        |
| --- | --------------------------------------------------------------- | --------------------------------------------------------------------- | ----------------------------------------------------------------------- | ----------------------- |
| 1   | 扫码枪键盘楔入：突发字符流（无触摸、~10–30ms 间隔）+ Enter 后缀 | `fill()+Enter` 近似                                                   | `page.keyboard` 突发注入（L2，DOM 近似）；`adb input`（L3，输入栈注入） | HID 电气时序/按键重复率 |
| 2   | ScanBar 焦点常驻 / 失焦回抢 / 浮层 `active=false` opt-out       | **部分覆盖**（已有 blur 回焦用例；缺同帧首字符竞态与 `active=false`） | DOM 层可覆盖（L2，见 §4.1；含产品修复项）                               | Android IME 层行为      |
| 3   | Capacitor 原生路径：`isNativePlatform`、APK 内无 dev proxy      | 不可                                                                  | Android 模拟器 + APK（L3，前置：统一网关入口 §5）                       | 厂商 ROM 差异           |
| 4   | 真实 `env(safe-area-inset-*)`（刘海/手势条）                    | 仅 fallback 最小值                                                    | AVD 挖孔屏镜像（L3，**不保证非零**，须环境前置断言 §5）                 | 真机刘海实测            |
| 5   | 弱网 / 断网 / 30s 超时（MAN-460 传输层）                        | 未覆盖                                                                | `context.setOffline` + CDP 节流 + route 故障注入（L2）                  | 真实蜂窝/漫游/DNS/TLS   |
| 6   | 真实后端数据与写路径（非 mock）                                 | 全 mock                                                               | L2 真实栈走查（man457 先例工程化）                                      | —                       |
| 7   | WebView 内核 vs 桌面 Chromium                                   | 不可                                                                  | AVD 系统 WebView（L3）                                                  | 厂商 ROM WebView        |
| 8   | 相机能力（拍照采集，能力门控两个分支）                          | 不可（**当前基线无相机实现**，MAN-458 未合入）                        | Web 路径 `filechooser.setFiles()`（L2，后置）；原生插件（L3）           | 真机摄像头              |
| 9   | 触感/单手可达/扫码枪握持姿势                                    | 不可                                                                  | 不可                                                                    | 保留                    |

**本表未列、暂归 L3 人工脚本或后续扩展的移动端差异**（评审补充）：Android 生命周期
（后台/前台、进程被杀、锁屏恢复、硬件 Back）、安装/覆盖升级（localStorage 保留、首启权限）、
横竖屏/字体缩放/Display Size/软键盘 VisualViewport、扫码协议边角（CR/LF/控制字符前后缀、
中文/超长码、快速连扫、重复码、多输入框竞争）、TLS 证书信任/CORS/mixed-content、
断网恢复后 refresh token 行为、冷启动/低内存。

## 3. 分层方案总览

| 层  | 名称                             | 状态 | 触发时机                                        |
| --- | -------------------------------- | ---- | ----------------------------------------------- |
| L0  | jsdom 单元/组件                  | 已有 | 每 PR（CI 门禁）                                |
| L1  | Playwright e2e（全 mock）        | 已有 | 每 PR（本地必跑；浏览器不可用按既有降级口径）   |
| L2  | **真实栈仿真走查（新增）**       | 新增 | 每个 PDA 业务 PR（本地栈可用时；核心交付）      |
| L3  | **Android 模拟器 + APK（新增）** | 新增 | 发版前必跑；触及 Capacitor/焦点/safe-area 的 PR |
| L4  | 实体真机冒烟（保留现清单）       | 保留 | 发版勾验（收缩须先积累对照证据，见 §6）         |

L2 依赖本地完整栈，**不进 CI 门禁**（与 `*PostgresProfileTests` 环境门控同一口径）；
为防「每 PR 必跑」退化成长期受阻项（与真机困境同构），配套：nightly/合并前最小 L2 smoke、
固定维护 owner、seed/schema 漂移失败 SLA。L3 首期本地执行，CI 化后置。

## 4. L2：真实栈仿真走查 harness（核心交付）

把 man457 那次一次性的真实栈走查工程化为可重复资产。

**形态**：`frontend/apps/business-pda/e2e-live/` 独立目录 + **独立
`playwright.live.config.ts`**（不与现有 `playwright.config.ts` 混用——后者
`fullyParallel: true` 且固定 `testDir: './e2e'`，真实后端共享 seed/admin/组织环境，
不适合并行）。live 配置约束：

- **无 `page.route` mock**：vite dev（双代理 → BusinessGateway 5119 + PlatformGateway 5100）
  - 真实 IAM admin 登录。
- `workers: 1`（或按独立 org/env 分片后再放开）；每次运行携带 `runId` 数据命名空间，
  seed 幂等且带 cleanup；独立 trace/report 输出。
- 显式命令运行（`pnpm e2e:live`），不进 CI；设备仿真沿用 Pixel 5 + 390×844 + touch。

### 4.1 扫码枪信号仿真器 `simulateScanGun()`

这是 owner 在 MAN-457 提过的「后期模拟信号检测」的落地。基于 `ScanBar.vue` 的行为契约
（焦点常驻、`@blur` 后 `requestAnimationFrame` 回抢、浮层场景 `active=false` 停止回抢、
`inputmode="none"` 不弹系统键盘、**仅处理 Enter 后缀**）：

```ts
// e2e-live/support/scan-gun.ts
interface ScanGunProfile {
  interCharDelayMs: number // 默认 15，模拟楔入突发
  suffix: 'Enter' // 与 ScanBar 现契约一致；Tab/控制字符后缀属契约扩展，另立 issue
  prefix?: string
}
async function simulateScanGun(page: Page, code: string, profile?: Partial<ScanGunProfile>)
```

关键：**不 `focus()`、不 `fill()`**，直接 `page.keyboard` 按 profile 时序打字符流——值必须
经由「焦点常驻」的前提进入 ScanBar。**定位声明：这是 DOM 层键盘楔入近似**（见 §2 保真等级）。
三个固定场景：

- **S1 常驻直扫**：页面加载后不做任何点击，直接扫 → 值进入 + `scan` 事件命中。
- **S2 失焦回抢竞态（产品缺陷探测器）**：现实现 blur 后**下一帧**才 `focus()`
  （`requestAnimationFrame(() => inputEl.value?.focus())`），RAF 之前到达的字符**没有接收者**
  ——首字符丢失是当前实现的真实风险，不是测试基建能绕过的问题。且 Playwright 两条命令间有
  协议往返，「不显式等待」无法稳定命中 RAF 窗口，朴素写法会假绿或随机红。因此 S2 用
  `page.evaluate` 在 blur 的同一任务内同步派发键盘事件序列，**确定性**复现竞态窗口；
  预期结论是**驱动产品修复**（候选：document/window 级扫码缓冲、明确 scanner mode），
  而非证明现状无恙。
- **S3 浮层不抢焦（含产品修复项）**：`refocus()` 只在**排入 RAF 前**检查一次 `props.active`
  ——若 blur 已排入 RAF 后 `active` 才变 false，回调仍会抢焦；且 `active` 从 false 恢复 true
  时无 watch 重新武装焦点。修复项：RAF 回调内复查 `active` + watch `active`（false 取消
  pending RAF / true 重新 focus）。现成失败样本：`design-system/gallery.vue` 同页渲染
  ScanBar 与 BottomSheet 却未传 `:active="!sheetOpen"`。S3 先落**组件契约测试**（L0/L1 层），
  再 live 逐页扫消费方是否漏传 `active`。

**因此 M1 不是纯 harness PR**：S2/S3 大概率携带 ScanBar 及消费页的产品代码修复。

同一 helper 附带浏览器手动走查用法（console 粘贴片段）；可选加一个
`import.meta.env.DEV` 门控的悬浮「模拟扫码」按钮（M2，供人工走查点按注码）。

### 4.2 网络/超时仿真（覆盖真机清单第 5 条的可自动化部分）

- `context.setOffline(true)` → 验 MAN-460 离线预检的类型化错误文案逐页透出
  （声明：只仿真 `navigator.onLine=false`，不代表 Wi-Fi 抖动/DNS/TLS/captive portal）。
- CDP 网络节流（注意 `Network.emulateNetworkConditions` 已被 CDP 标 deprecated，封装适配层、
  仅 Chromium）→ loading 态不闪断。
- 超时路径**不真等 30s**：timeout 值做成可注入（env/配置），live 测试注入短超时（如 2s）+
  `page.route` 故障注入，分别覆盖「请求整体挂起」与「headers 已到、body 卡死」两种形态，
  验 AbortController 超时文案（区别于导航取消不误报）。
- 幂等断言不止 UI 不重复提交：**捕获重试前后两次请求的幂等键相同 + 从真实后端回读只落一条事实**。

### 4.3 相机（后置：待 MAN-458 `@capacitor/camera` 合入基线后启动）

当前基线（main）**无相机插件与采集实现**，本节在其合入前不排期。合入后按三条路径：

- **Web 降级路径**（`<input type=file capture>`，非 `getUserMedia`）：用 Playwright
  `filechooser.setFiles()` 喂测试图片——fake media stream flags 对文件选择器无效，不适用。
- **能力门控双分支**：显式注入能力探针/权限结果（mock `Capacitor.isNativePlatform` 与
  `mediaDevices` 存在性），分别断言入口渲染与整体隐藏；不依赖「有无 fake device」间接推断。
- **原生 `Camera.getPhoto` 路径**：归 L3（模拟器虚拟相机 + 人工或 Android-native UI 自动化；
  原生权限弹窗不属于 WebView Page，Playwright 驱不动）。

### 4.4 数据前置

- **优先真实 seed**：`QualitySeedService` 先例推广，各域缺口逐个补 seed service 或
  内部造数端点（报警域 5116 POST 先例）；seed 携带 `runId` 命名空间、幂等、带 cleanup。
- **兜底 `setQueryData` 缓存注入**（man457 已验证配方）：仅用于读面 UI 验证，
  **不得作为真实栈证据**，走查记录中必须标注注入而非真数据。

### 4.5 运行脚本与证据口径

- `frontend/apps/business-pda/scripts/pda-live-walkthrough.ps1`（pwsh）串起：
  worktree 归属检查（不误测并行会话的栈）→ 起/复用栈（现行 `nerv.ps1 dev`；PR#917 并行
  隔离方案落地后迁移到隔离入口）→ seed → 跑 live spec → 证据落
  `DESIGN/roadmaps/assets/<date>-<topic>/`。
- **证据包**（截图不得单独构成 L2 通过证据）：commit/分支指纹 + Playwright trace +
  关键请求 URL/status（写操作含幂等键）+ **写操作后端状态回读** + 截图。
  截图控制数量，避免二进制膨胀。
- 走查记录沿用 man457 模板：环境表 + 步骤/断言/证据表 + 「已覆盖 / 未覆盖（留待 L3/L4）」
  声明，杜绝层级虚报。

## 5. L3：Android 模拟器 + APK 层

- **前置（P0）：统一网关入口**。当前 vite dev 是双代理（`/api/business-console`→5119、
  `/api/console`→5100），而 APK 只有一个全局 `VITE_NERV_IIP_API_BASE_URL`——单指 5119 会把
  登录/refresh/me（`/api/console`）打到 BusinessGateway 上 404。L3 开工前须先交付一个
  同时代理两段路径的统一 ingress（本地反代或网关聚合），APK 基址只指向该入口
  （模拟器内宿主机别名 `10.0.2.2`）。
- **cleartext/mixed-content（P0）**：`capacitor.config.ts` 的 `androidScheme: 'https'` 与
  `http://10.0.2.2` 入口冲突——Android 9+ 默认禁 cleartext，安全源访问 HTTP 还会被
  mixed-content 拦截。须提供 **debug-only Network Security Config**（并设「不得进入
  release APK」门禁）或可被 AVD 信任的 HTTPS 测试入口；同时验证网关 CORS 允许
  Capacitor origin。
- **构建**：按 `mobile-pda-deployment.md` 口径 `cap add android` 确定性再生 →
  `gradlew assembleDebug`。工具链锁定不止「JDK 17 + Android SDK」：**固定 system image /
  API level / build-tools / emulator / WebView milestone / AVD 配置**，并明确 debug NSC 等
  原生改动如何在每次 `cap add android` 后稳定重放（脚本化 patch）。
- **safe-area 不保证非零**：Android WebView 对 CSS safe-area 的支持受 WebView 版本与
  window-insets 传递方式影响，挖孔镜像 ≠ `env(safe-area-inset-*)` 非零。做法：启动后先记录
  WebView 版本 + 四个 computed inset，**inset 非零作为环境前置断言**——不满足则如实报
  「环境不具备该能力」，不得继续假验。
- **扫码仿真**：`adb shell input text '<code>' && adb shell input keyevent 66`——定位为
  「Android 输入栈注入」（经 IME/焦点系统），比浏览器 `page.keyboard` 更接近，但仍非
  USB HID 硬件等价（无 scan code/device id）。若未来接 Zebra DataWedge，改
  `adb shell am broadcast` intent 仿真，helper 留扩展位。
- **相机**：模拟器虚拟相机 → `Capacitor.isNativePlatform === true` 分支真点亮（待 §4.3 前置合入）。
- **断言与存证**：首期「脚本化构建安装 + 人工按 L4 清单勾验 + `adb exec-out screencap` 存证
  - APK SHA256/AVD/WebView 版本指纹」；自动化归 M4 spike（见 §8）。
- **触发**：发版前必跑（且周期性冷启动跑，不只发版当天首跑）；触及 Capacitor 配置、
  网关基址、扫码焦点逻辑、safe-area 的 PR 必跑。

## 6. L4 真机清单：暂不收缩

v1 曾提议收缩为 3 项，评审驳回：在 L2/L3 尚未运行、更未积累「L2/L3 通过 ↔ L4 实测一致」的
对照证据前收缩是自欺。**现行 5 条清单原样保留**，并按 §2 补充项酌情增补（安装/升级、
横竖屏/后台恢复、相机权限与返回）。待 L2/L3 稳定运行**至少两个发版周期**且对照无漏后，
再提收缩议案。**口径不变**：L2/L3 是「真机模拟检测」，不改写「真机」定义；发版门仍是 L4。

## 7. PR 交付达标口径（修订建议）

| PR 类型                                          | 最低要求                                                                                     |
| ------------------------------------------------ | -------------------------------------------------------------------------------------------- |
| 纯 PDA 前端业务 PR                               | L0/L1 门禁 + **L2 走查跑绿 + §4.5 证据包**；栈不可用时如实标注受阻原因（不伪造、不降级虚报） |
| 触及 Capacitor / 扫码焦点 / safe-area / 网关基址 | 追加 L3                                                                                      |
| 发版                                             | L3 全量 + L4 清单勾验                                                                        |

PR body 固定声明格式：`走查层级：L2（真实栈仿真）｜证据：<走查记录链接>｜未覆盖：<留待 L3/L4 项>`。

## 8. 分期交付（v2：M1 按评审拆三段）

- **M1a（产品修复 + 契约）**：ScanBar 焦点契约修复（RAF 回调复查 `active`、watch `active`
  取消/重arm、S2 首字符竞态的产品级答案——document 级缓冲或 scanner mode 裁决）+
  组件契约测试 + gallery `:active` 失败样本修复 + 消费页 `active` 全扫。
- **M1b（live harness 骨架）**：`playwright.live.config.ts` + `simulateScanGun`（Enter only）+
  真实登录 + quality 链路 1 条**只读** live spec + `pda-live-walkthrough.ps1` +
  更新 `mobile-pda-testing-and-smoke.md`（含 5 spec/25 用例基线纠偏）。
- **M1c（写路径）**：1 条真实写路径 live spec：提交 + 幂等键捕获 + 后端状态回读。
- **M2**：网络仿真拆分 spec（offline / 慢网 / 整体挂起 / headers-后-body-stall，短超时注入
  不真等 30s）+（可选）dev 悬浮模拟扫码件。相机移出 M2，待 MAN-458 合入后另排（§4.3）。
- **M3a（L3 前置）**：统一双网关 ingress + debug-only NSC + CORS 验证 + APK build/install smoke。
- **M3b**：固定 AVD/system image/WebView 版本 + adb 扫码/截图脚本 + safe-area 前置断言 +
  扫码/旋转/Back/生命周期人工脚本 + 发版 checklist 更新。
- **M4（spike，可弃）**：Playwright experimental Android 驱动 Capacitor WebView 验证性试验；
  不预设「复用现有 spec」收益（需 ADB fixture/WebView debugging，原生弹窗驱不动，release APK
  通常不开 WebView debugging）；spike 失败即转 UiAutomator/Appium 评估，不阻塞 L3 人工层。

**风险**：L2 依赖本地栈与 seed（不进 CI，同 Postgres 集成测试口径，靠 §3 的 nightly smoke +
owner + SLA 防漂移）；AVD 对 CI 太重（先本地）；M4 属实验 API（独立、可弃）。

## 9. 明确不做（仍属 L4 或超范围）

实体扫码枪 HID 电气时序与按键重复率、厂商 ROM WebView、真机触感/握持/单手可达、
真实刘海设备差异、iOS（当前无目标设备）。

## 10. 评审记录

2026-07-15 codex（gpt-5.6-sol，reasoning effort=high，read-only）对抗式评审，裁决
「需修订后落地」。P0 意见 8 条全部经本地代码核实属实并吸收进本版：L1 基线计数纠偏（§1）、
焦点回抢改部分覆盖 + 保真等级声明（§2）、Tab 后缀删除 + S2/S3 标产品修复（§4.1）、
相机改 filechooser 并后置（§4.3）、统一 ingress + cleartext/NSC 前置（§5）、safe-area
前置断言（§5）、L4 暂不收缩（§6）、证据包替代裸截图（§4.5/§7）。P1 分期拆分已吸收（§8）。
未采纳项：无。
