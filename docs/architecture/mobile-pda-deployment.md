# PDA（business-pda）部署：Capacitor / APK 基线

> 事实依据：`frontend/apps/business-pda/`（`package.json` 脚本、`.env.example`、`src/main.ts`、
> `capacitor.config.ts`）、`frontend/packages/api-client/src/transport/{base-url,client-config}.ts`、
> `frontend/pnpm-lock.yaml`（@capacitor/* 锁定版本）。
>
> 配套：测试层次见 `docs/architecture/mobile-pda-testing-and-smoke.md`；
> 端架构见 `docs/architecture/mobile-pda-capacitor-architecture.md`。

本文档说明 PDA 的 **配置 + 脚本基线** 如何提交到仓库，以及在 **干净环境** 里如何可复现地产出 APK。

## 1. Capacitor / APK 网关基址（VITE_NERV_IIP_API_BASE_URL）

### 为什么必须设

api-client 的 `configureApiClient` 在未显式传 `baseUrl` 时回退到 `getApiBaseUrl()`，后者读
`VITE_NERV_IIP_API_BASE_URL`（见 `frontend/packages/api-client/src/transport/base-url.ts`）。
business-pda 的 `src/main.ts` 显式传 `baseUrl: import.meta.env.VITE_NERV_IIP_API_BASE_URL || undefined`，
与该默认行为**完全一致**（`options.baseUrl ?? getApiBaseUrl()`）：

| 运行形态 | `VITE_NERV_IIP_API_BASE_URL` | 实际基址 | 转发方式 |
| --- | --- | --- | --- |
| Web / 本地开发 | 留空 | 相对 `/api/...` | vite dev proxy（`vite.config.ts` 的 `server.proxy`）转发到本地网关 |
| Capacitor / APK | **绝对网关地址** | `https://<gateway>/api/...` | WebView 直接请求绝对地址（**无 dev proxy**） |

APK 内的 WebView 源是 `capacitor://` / `https://localhost`，**没有 vite dev proxy**。
若沿用相对 `/api/...`，请求会落到 WebView 自身源而非真实网关，必然失败。
因此 **打 APK 前必须把 `VITE_NERV_IIP_API_BASE_URL` 设为绝对的
BusinessGateway / PlatformGateway 基址**，并在 `vp build` 时注入。

### 怎么设

构建产物在 `vp build` 阶段就已把 `import.meta.env.VITE_NERV_IIP_API_BASE_URL` 内联，
所以环境变量必须在 **build 之前** 就位（`.env.production`、`.env.local`、CI 变量或临时 shell 变量均可）。
模板见 `frontend/apps/business-pda/.env.example`。

```bash
# 示例：在 apps/business-pda 下，注入网关地址再构建 + 同步原生工程
# Unix
VITE_NERV_IIP_API_BASE_URL=https://your-gateway-host pnpm run cap:sync
# Windows (PowerShell)
$env:VITE_NERV_IIP_API_BASE_URL = 'https://your-gateway-host'; pnpm run cap:sync
```

> `cap:sync` = `vp build . && cap sync android`。务必让该变量在 `vp build` 时可见，
> 否则产出的 APK 仍指向相对路径、真机内无法连网关。

## 2. APK 基线的可复现构建（干净环境）

### 提交进仓库的是「配置 + 脚本」，不是 `android/`

`frontend/apps/business-pda/android/` 在 `.gitignore` 中**有意忽略**：它是由
`cap add android` 依据 `capacitor.config.ts` + 已锁定的 `@capacitor/*` 版本**确定性再生**的原生壳工程，
不作为源码维护。仓库提交的基线是：

- `package.json` 中**精确锁定**的 `@capacitor/*` 版本（无 `^` 脱字号，与 `pnpm-lock.yaml` 解析版本一致）；
- `capacitor.config.ts`、`.env.example`、`cap:sync` / `cap:open` 脚本。

给定相同工具链，`cap add android` 的产物是确定的，因此 `android/` 无需入库。
APK 的真正产出仍需下文的 Android 工具链。

### 前置工具链

- **JDK 17**（Capacitor 8 / 现代 Android Gradle Plugin 要求）。
- **Android SDK**，并设好 `ANDROID_HOME`（或 `ANDROID_SDK_ROOT`）环境变量。
- Node + pnpm（仓库根 `frontend/` 工作区）。

### 有序命令（干净环境复现）

```bash
# 1) 安装工作区依赖（锁定版本，确定性）
pnpm -C frontend install

# 以下命令在 frontend/apps/business-pda 目录下执行：

# 2) 生成原生 Android 壳工程（确定性，依据 capacitor.config.ts + 锁定的 @capacitor/*）
pnpm exec cap add android

# 3) 构建 web 产物并同步进原生工程
#    （记得按上一节注入 VITE_NERV_IIP_API_BASE_URL，否则 APK 连不上网关）
pnpm run cap:sync

# 4) 用 Gradle 打 Debug APK —— 平台相关、手动步骤：
#    Unix / macOS：
cd android && ./gradlew assembleDebug
#    Windows：
cd android && .\gradlew.bat assembleDebug
```

产物：`android/app/build/outputs/apk/debug/app-debug.apk`。

### 为什么打 APK 不在 npm 脚本里

`cap:apk` 旧脚本里写死了 `cd android && ./gradlew assembleDebug`，在仓库的
**Windows 优先**环境里会静默失败（Windows 上是 `gradlew.bat`，没有可执行的 `./gradlew`）。
为避免「跑了却不报错」，该步骤**不再封装为脚本**，而是作为上面的**平台相关手动命令**记录，
Unix 用 `./gradlew assembleDebug`、Windows 用 `.\gradlew.bat assembleDebug`。
仓库脚本只保留跨平台安全的 `cap:sync`（`vp build . && cap sync android`）与 `cap:open`。
