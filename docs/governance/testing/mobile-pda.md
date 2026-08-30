# PDA 测试治理

本文定义 `frontend/apps/business-pda` 各测试层能够证明什么，以及自动化、live、Android 模拟器/APK 与物理设备 smoke 之间不得越界的结论。当前 spec/用例清单以代码、Playwright/Vitest 配置和 runner discovery 为准，不在 Governance 维护手工计数。

## 分层证明范围

| 层 | 可以证明 | 不能据此证明 |
| --- | --- | --- |
| jsdom/unit/component | 组件/store/路由守卫、数据转换与确定性 UI 逻辑。 | 浏览器真实布局、计算样式、安全区、真实触控、后端或真机。 |
| mock Playwright | Chromium 中的真实 DOM/导航/交互/布局 smoke，以及前端对 mock Gateway 契约的消费。 | 真实后端、FullChain、Capacitor bridge、Android WebView、实体扫码枪/相机/IME。 |
| live stack/browser | 当前真实服务/身份/公开入口下的业务路径和网络集成。 | APK/WebView/设备权限、硬件扫码、相机、系统 IME 和物理设备差异。 |
| Android emulator + APK | 构建产物、WebView/Capacitor、Android 权限/系统交互在模拟设备上的行为。 | 具体厂商 PDA 硬件、扫描头、摄像头、企业网络和现场人体工学。 |
| physical-device smoke | 指定设备/系统/版本/网络下的真实扫码、相机、IME、安全区、触控和核心业务路径。 | 全量回归、其它设备矩阵或自动化层已经覆盖的所有业务语义。 |

mock 浏览器即使 UI 完整也不能称为 FullChain；live HTTP 流程若没有 APK/真实 WebView 也不能称为真机验证。物理 smoke 不能替代可重复自动化层。

## 当前稳定要求

- 深链与写操作只消费服务端确认的强身份、scope 与 allowed action；过期、歧义、未知或越权输入 fail closed。
- route/principal/organization/environment/scope 改变后，迟到的旧请求或 mutation 结果不得覆盖当前 identity。
- 关键触控、焦点、键盘、overlay、安全区、暗色和无横向溢出由合适的真实浏览器/设备层证明，不能由 jsdom 代替。
- 扫码预校验失败不得产生业务写；成功写入仍以服务端当前授权和业务合同为准。
- UI mock 测试的 fixture 只能表示公开契约，不得把当前页面实现输出反向当成合同来源。

当前测试文件与 producer 导航见 [`../../reference/testing/mobile-pda-inventory.md`](../../reference/testing/mobile-pda-inventory.md)。操作 smoke 见 [`../../runbooks/testing/mobile-pda.md`](../../runbooks/testing/mobile-pda.md)，历史 live/emulator/真机形成过程见 [`../../reports/audits/mobile-pda-testing-evolution-2026-08.md`](../../reports/audits/mobile-pda-testing-evolution-2026-08.md)。
