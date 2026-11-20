# Testing Runbook 入口

本目录只说明测试治理下的当前可执行操作；规则仍由 `docs/governance/testing/` 拥有。

- [`evidence.md`](evidence.md)：本地/CI evidence 取证与结果判读。
- [`determinism.md`](determinism.md)：确定性 checker/verifier 与问题定位。
- [`real-dependencies.md`](real-dependencies.md)：PostgreSQL、Redis/CAP、FullChain 本地运行与残留回收。
- [`mobile-pda.md`](mobile-pda.md)：business-pda 自动化、live、模拟器/APK 与真机 smoke。
- [`frontend-vitest-local.md`](frontend-vitest-local.md)：本地前端 vitest 跑法与红结果归因（资源争用假红 vs 真红）。

命令参数最终以脚本 `Get-Help`、package scripts、workflow 和当前代码为准。

## Business Console 第五、六轮手工取证

使用已启动的本地 leader-demo 栈。执行前设置 `NERV_IIP_PLAYWRIGHT_BASE_URL` 和
`NERV_IIP_FULLSTACK_ADMIN_PASSWORD`，并按下表设置产物路径。提前创建
`NERV_IIP_R5_OUT_DIR` 下的 `reports/` 子目录，以及两个 r6 JSON 输出文件的父目录。

从仓库根目录运行：

```sh
pnpm -C frontend --filter @nerv-iip/business-console e2e:evidence --project=desktop --workers=1
```

可追加文件名选择单个脚本，或追加 `--list` 仅查看选取结果。使用 desktop 取证可避免两个项目覆盖同名产物。

| 脚本 | 产物变量 |
| --- | --- |
| `r5-walkthrough`、`r5-scheduling`、`r5-final` | `NERV_IIP_R5_OUT_DIR`，截图和报告目录 |
| `r6-sweep` | `NERV_IIP_R6_SWEEP_DIR`，截图和报告目录 |
| `r6-lots-probe` | `NERV_IIP_R6_LOTS_OUT`，JSON 文件路径 |
| `r6-timeline-probe` | `NERV_IIP_R6_PROBE_OUT`，JSON 文件路径 |
| `r6-warn-check` | 告警记录写到终端 |
