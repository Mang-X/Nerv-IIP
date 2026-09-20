# Testing Runbook 入口

本目录只说明测试治理下的当前可执行操作；规则仍由 `docs/governance/testing/` 拥有。

- [`evidence.md`](evidence.md)：本地/CI evidence 取证与结果判读。
- [`determinism.md`](determinism.md)：确定性 checker/verifier 与问题定位。
- [`real-dependencies.md`](real-dependencies.md)：PostgreSQL、Redis/CAP、FullChain 本地运行与残留回收。
- [`mobile-pda.md`](mobile-pda.md)：business-pda 自动化、live、模拟器/APK 与真机 smoke。
- [`frontend-vitest-local.md`](frontend-vitest-local.md)：本地前端 vitest 跑法与红结果归因（资源争用假红 vs 真红）。

命令参数最终以脚本 `Get-Help`、package scripts、workflow 和当前代码为准。
