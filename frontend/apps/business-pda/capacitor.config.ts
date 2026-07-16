import type { CapacitorConfig } from '@capacitor/cli'

// 本文件由 Capacitor CLI 在 `cap sync` 时以 Node 求值（不在 app 的 vue-tsc tsconfig include 内），
// 这里自带最小 process 声明，避免引入 @types/node。
declare const process: { env: Record<string, string | undefined> }

// dev 调试 APK 分叉开关（方案 §5 / §8 M3a，L3 dev 冒烟口径）：
// NERV_PDA_DEV_APK=1 时（仅由 scripts/pda-apk-build.ps1 在构建期注入）：
//   - androidScheme 'http'：WebView 源变为 http://localhost，避免 https 安全源访问
//     http://10.0.2.2 统一入口时被 mixed-content 拦截；
//   - cleartext true：允许 WebView 明文流量（Android API 28+ 默认禁 cleartext），
//     否则打到 http://10.0.2.2:5126 的请求会被系统网络栈直接拒绝。
// 注意（预期行为，非 bug）：scheme 变更等价于换域——dev APK（http://localhost）与
// release APK（https://localhost）的 localStorage / cookie / IndexedDB 互不相通，
// 两种 APK 之间不共享登录会话等本地状态。
// 默认（未设该 env，含所有 release / 生产构建路径）保持 androidScheme 'https'、
// cleartext 缺省 false，release 行为与既有口径完全一致。
// fail-closed 保障（脚本侧，见 scripts/pda-apk-build.ps1）：-ReleaseProfile 构建前显式
// 清除本 env（防手工/CI 残留把 release 悄悄切到 http+cleartext），构建后用 aapt2 断言
// manifest usesCleartextTraffic + 解包 assets/capacitor.config.json 断言 androidScheme
// 与 profile 一致，不一致 exit 1——本文件的分叉不是唯一防线。
const devApk = process.env.NERV_PDA_DEV_APK === '1'

const config: CapacitorConfig = {
  appId: 'com.nerviip.pda',
  appName: 'Nerv-IIP 手持作业台',
  webDir: 'dist',
  server: devApk
    ? {
        androidScheme: 'http',
        cleartext: true,
      }
    : {
        androidScheme: 'https',
      },
}

export default config
