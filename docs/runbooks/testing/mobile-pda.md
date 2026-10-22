# PDA 测试与 Smoke Runbook

证明边界见 [`../../governance/testing/mobile-pda.md`](../../governance/testing/mobile-pda.md)，当前 spec/命令入口见 [`../../reference/testing/mobile-pda-inventory.md`](../../reference/testing/mobile-pda-inventory.md)。

## 自动化

1. 单元/组件测试从 `frontend/apps/business-pda/package.json` 的当前 test script 执行。
2. 浏览器 e2e 从当前 Playwright 配置/脚本执行；缺浏览器或依赖时按 runner 失败，不把未运行写成通过。
3. mock e2e 结果标记为 mock browser 证据；需要真实后端时切到当前 live/full-chain 入口，不修改 fixture 让页面“看起来像真实”。

## Android 模拟器 / APK

- APK 构建、Capacitor sync、Android 配置和产物以 `mobile-pda-deployment.md` 与当前 build script 为准。
- 记录 commit、APK fingerprint、emulator/device API、viewport/rotation、权限与目标环境；失败后先区分 WebView/Capacitor、权限、网络和业务 API。

## 物理设备 smoke

至少记录设备型号、OS/WebView/应用版本、网络环境和测试账号/组织环境（不记录秘密），再按当前业务优先级验证：启动/登录、扫码输入、关键写操作、错误/越权 fail-closed、键盘/IME、触控、安全区，以及需要时的相机/权限。

真机 smoke 是指定设备上的 release/device 证据，不替代 unit/e2e/live 自动化，也不能外推到未测设备矩阵。
