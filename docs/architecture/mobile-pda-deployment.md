# PDA（business-pda）部署：Capacitor / APK 基线

> 事实依据：`frontend/apps/business-pda/`（`package.json` 脚本、`.env.example`、`src/main.ts`、
> `capacitor.config.ts`）、`frontend/packages/api-client/src/transport/{base-url,client-config}.ts`、
> `frontend/pnpm-lock.yaml`（@capacitor/\* 锁定版本）。
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

| 运行形态        | `VITE_NERV_IIP_API_BASE_URL` | 实际基址                    | 转发方式                                                           |
| --------------- | ---------------------------- | --------------------------- | ------------------------------------------------------------------ |
| Web / 本地开发  | 留空                         | 相对 `/api/...`             | vite dev proxy（`vite.config.ts` 的 `server.proxy`）转发到本地网关 |
| Capacitor / APK | **绝对网关地址**             | `https://<gateway>/api/...` | WebView 直接请求绝对地址（**无 dev proxy**）                       |

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

### dev 调试 APK：统一入口 `http://10.0.2.2:5126`（L3 dev 冒烟口径）

生产 APK 的基址口径不变（指真实网关）。但 **L3 dev 冒烟**（Android 模拟器 + debug APK，
方案 `frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md` §5 / §8 M3a）有一个
双网关难题：APK 只有一个全局基址，单指 BusinessGateway(5119) 会把登录/refresh/me
（`/api/console`）打到错误网关 404。

解法是把基址指向 **宿主 vite dev 本身**：`vite.config.ts` 的 `server.proxy` 同时代理
`/api/business-console` → 5119 与 `/api/console` → 5100，天然就是统一 ingress；模拟器内
`10.0.2.2` 是宿主机回环别名（vite dev 绑定 `127.0.0.1:5126` 可达）。api-client 生成客户端按
「绝对基址 + `/api/...` 路径」拼接（`generated/core/utils.gen.ts` 的
`getUrl`：`url = baseUrl + pathUrl`），因此
`VITE_NERV_IIP_API_BASE_URL=http://10.0.2.2:5126` 时请求落到
`http://10.0.2.2:5126/api/console/...`，正中 vite 代理路径。CORS 无需额外配置：dev APK 的
WebView 源是 `http://localhost`（见下节 scheme 分叉），落在 vite dev server 默认
CORS 允许的 localhost 白名单内。

约束（如实声明，勿虚报为发版口径）：

- 这是 **dev 冒烟口径**，要求宿主机上 **vite dev（5126）与后端栈（`nerv.ps1 dev`）同时在跑**；
- 生产/发版 APK 仍按上一节口径把基址指向真实网关（HTTPS）。

### debug 分叉开关 `NERV_PDA_DEV_APK=1`（cleartext / androidScheme，release 不受影响）

`http://10.0.2.2` 是明文入口，与默认 `androidScheme: 'https'` 冲突两处：Android API 28+
默认禁 cleartext（系统网络栈直接拒绝 HTTP 请求）；https 安全源访问 HTTP 还会被
mixed-content 拦截。`capacitor.config.ts` 是 TS，在 `cap sync` 时由 Capacitor CLI 以 Node
求值，因此按构建期 env 分叉：

- `NERV_PDA_DEV_APK=1`（仅 `scripts/pda-apk-build.ps1` dev 路径注入）：
  `androidScheme: 'http'` + `cleartext: true`；
- 未设（默认，含所有 release/生产构建路径）：保持 `androidScheme: 'https'`、cleartext
  缺省 false，与既有口径逐字节一致。

**预期取舍（须知，非 bug）**：Capacitor 官方文档明确「改 scheme 等价于换域」——dev APK
（`http://localhost` 源）与 release APK（`https://localhost` 源）的
localStorage / cookie / IndexedDB 互不相通，登录会话等本地状态在两种 APK 之间不共享。

### 一键构建脚本 `scripts/pda-apk-build.ps1`（实测口径）

`frontend/apps/business-pda/scripts/pda-apk-build.ps1`（pwsh 7；治理头 Category: generate）
串起整条管线，幂等可重跑：

```powershell
# 工具链定位：显式 env（ANDROID_HOME/ANDROID_SDK_ROOT、JAVA_HOME）优先；未设时脚本会
# 自动探测约定安装位置（SDK：%USERPROFILE%\android-sdk、%LOCALAPPDATA%\Android\Sdk；
# JDK：%USERPROFILE%\.jdks、Program Files\Eclipse Adoptium/Java 里主版本最高的 21+），
# 新终端/新会话零配置可跑；显式 JAVA_HOME 若低于 21 也会回落到探测（WARN 提示）。
pwsh -File frontend/apps/business-pda/scripts/pda-apk-build.ps1            # dev 冒烟 APK（默认）
# 按真实 HTTPS 网关基址打包（不注入 dev 分叉；产物仍是 debug 签名包）：
pwsh -File frontend/apps/business-pda/scripts/pda-apk-build.ps1 -ApiBaseUrl https://your-gateway-host -ReleaseProfile
# 强制删除 android/ 全量再生：
pwsh -File frontend/apps/business-pda/scripts/pda-apk-build.ps1 -Recreate
```

步骤：工具链定位（显式 env 优先，未设时自动探测约定位置；探测不到才失败）与
node_modules 前置检查 →
注入构建期 env（基址 + 分叉开关）→ `pnpm run build`（vue-tsc + vp build → dist/）→
`android/` 缺失时 `cap add android` 确定性再生 → `cap sync android` →
`gradlew assembleDebug`（脚本内已处理 Windows `gradlew.bat` 与 Unix `./gradlew` 差异）→
输出 APK 路径 + SHA256 + 构建指纹（commit/时间/基址/分叉），指纹落
`android/app/build/outputs/apk/debug/build-fingerprint.txt`（gitignored）。

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

- **JDK 21+**（实测口径：Capacitor 8 的 android 库 `sourceCompatibility = 21`，
  JDK 17 会在 `:capacitor-android:compileDebugJavaWithJavac` 上报「无效的源发行版：21」；
  Gradle 8.14 自身最高支持 Java 24，别用更新的 JDK 当 daemon JVM）。
- **Android SDK**：设 `ANDROID_HOME`（或 `ANDROID_SDK_ROOT`），或装在约定位置
  （`%USERPROFILE%\android-sdk` / `%LOCALAPPDATA%\Android\Sdk`）由脚本自动探测
  （compileSdk 由 Capacitor 8 锁定为 36，缺失时 Gradle 会经 SDK 组件自动安装拉取）。
- Node + pnpm（仓库根 `frontend/` 工作区）。
- 仓库路径含非 ASCII（中文）目录时，AGP 路径检查须以 `android.overridePathCheck=true`
  显式关闭（`pda-apk-build.ps1` 会自动在再生的 `android/gradle.properties` 里补上；实测可正常出包）。

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
为避免「跑了却不报错」，该步骤**不封装为 npm 脚本**：手动路径按上面的**平台相关命令**执行，
Unix 用 `./gradlew assembleDebug`、Windows 用 `.\gradlew.bat assembleDebug`；脚本化路径用
`scripts/pda-apk-build.ps1`（pwsh 内区分平台调用对应 wrapper，失败即报错退出，见第 1 节）。
npm 脚本只保留跨平台安全的 `cap:sync`（`vp build . && cap sync android`）与 `cap:open`。
