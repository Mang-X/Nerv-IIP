# business-pda 测试生产者导航

| 层 | 当前入口 / producer |
| --- | --- |
| unit / component | `frontend/apps/business-pda/package.json`、`src/**/*.test.*` / 当前测试配置 |
| browser e2e | `frontend/apps/business-pda/playwright.config.ts`、`frontend/apps/business-pda/e2e/` |
| mock Gateway fixture | `frontend/apps/business-pda/e2e/fixtures.ts` 及各 spec 自有 route fixture |
| live stack | 当前 business-pda live/e2e 配置、Gateway/服务公开入口与相应 CI/本地启动入口 |
| Android / Capacitor | `frontend/apps/business-pda/android/`、Capacitor 配置、`scripts/pda-apk-build.ps1` 等当前 build producer |
| PDA 产品/交互设计 | `docs/product/mobile-pda/` 与当前页面代码 |

不要在本文维护“当前 N 个 spec / M 个用例”的手工数字；需要盘点时以 runner discovery、目录和当前 commit 为基线。设备型号、真机结果与一次性 live/emulator 证据进入运行记录或冻结 audit，不成为当前 inventory。
