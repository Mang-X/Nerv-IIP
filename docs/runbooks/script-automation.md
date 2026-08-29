# 脚本与自动化操作 Runbook

本页只描述当前操作者如何选择、执行和排障受治理脚本。规则边界见 [`../governance/script-automation.md`](../governance/script-automation.md)；命令、参数、scenario、artifact 路径和状态字段最终以 `nerv.ps1 help`、目标脚本 `Get-Help`、源码与当前测试为准。

## 1. 修改脚本前

1. 读取目标路径适用的 `AGENTS.md` 与脚本 Governance。
2. 查看目标脚本头部 `Script-Governance` 声明，确认 Category、SideEffects、Writes、Cleanup、Requires 与准备修改的行为一致。
3. 用 `Get-Help <script> -Full`、`pwsh <script> -?` 或 `nerv.ps1 help` 确认当前入口和参数，不从旧计划/报告复制命令。
4. 识别本次 invocation 拥有的 database/container/process/session/artifact；所有权无法证明时不要执行 destructive 操作。
5. 涉及客户数据、migration、backup、restore 或 seed 时转到 [`database-release.md`](database-release.md)，不要用 verify/fullstack 入口替代 release-install。

## 2. 修改后的快速验证

至少执行：

```powershell
pwsh scripts/check-script-governance.ps1
```

随后运行目标脚本对应的**已有**契约测试。若改动触及 checker、library scan boundary、dynamic invocation/binding、ordinal comparison、CI impact plan、test evidence 或其它共享 producer，必须运行其现有 tests；不要为普通文档/脚本修改新造自然语言 checker 或一次性 fixture。

提交/PR 中只登记真实执行结果。某个 provider、FullChain、Redis/CAP 或 backend shard 被 CI policy skip 时必须写 `skipped`，不能用聚合 job 绿色替代。

## 3. 执行有副作用的 verify/fullstack

1. 通过公开入口创建或解析精确 session/target；当前 scenario 名称从 `nerv.ps1 help` 或脚本帮助读取。
2. 确认将写入/删除的数据库、容器、volume、端口、seed、文件和进程都属于本次 session。
3. 启动后保存当前脚本产生的 session/run 身份和非敏感 manifest/evidence 路径。
4. 失败时先读该次 invocation 的 stdout/stderr、exit code、duration、root PID、cleanup 状态和当前脚本给出的诊断。
5. 无论成功失败都让受治理入口完成自己的 `finally`/cleanup；需要人工补救时也只操作 manifest/owner 能证明属于本次运行的资源。
6. 禁止 `aspire stop --all`、Docker prune、按名称前缀批量删除数据库/volume、按进程名扫杀等扩大清理。

交互式 start 只用于诊断；交接或结束任务前停止该会话拥有的资源。

## 4. 识别 signal / timeout / 普通失败

出现 `NERV-SIGNAL-EXIT` 时，它表示某层捕获到了受信号终止的子进程信息。排障时：

1. 保留 marker 中的 signal/exit code，不把它改写成普通断言失败；
2. 继续查看当前 invocation 的子进程日志与资源证据；
3. signal 只证明进程如何终止，不自动证明 OOM、测试失败、业务断言失败或基础设施抖动的根因；
4. timeout 同样先确认被停止的是本次 invocation 的进程树，再从 stdout/stderr/资源证据定位原因。

历史 signal/memory 事故见 [`../reports/investigations/script-automation-signal-memory-2026-08.md`](../reports/investigations/script-automation-signal-memory-2026-08.md)，不要直接执行其中旧命令。

## 5. Governance 失败的常见排查

### Header / Category

- `MissingGovernanceHeader` / `MissingCategory`：补全真实声明，不要填空壳。
- `InvalidCategory` / library 分类错误：核对文件实际是否为 `scripts/lib/` library，以及是否被作为程序入口调用。

### 原生命令或进程启动

若 checker 报 direct command、dynamic invocation 或 process start：

1. 先检查 `scripts/lib/ScriptAutomation.ps1` 是否已经有适合 helper；
2. 有则复用，不写近似 wrapper；
3. 没有时，只有任务真正拥有共享 helper 契约时才扩展它，并同步已有 tests；
4. 不通过 baseline exemption 规避新代码问题。

### library 动态 seam

`& $Action` 只有 checker 能在当前作用域链静态证明它是 script block seam 时才允许。若被拒绝，优先把依赖显式建模成 `[scriptblock]` 参数或清晰的 script-block binding；不要靠动态字符串、scope-qualified 变量、`Get-Variable` 或其它运行时技巧绕过静态证明。

### Ordinal 比较

身份/名称/路径/SHA/lane/status 等治理字符串出现 culture-aware finding 时，使用显式 `StringComparison.Ordinal/OrdinalIgnoreCase`、`StringComparer.Ordinal/OrdinalIgnoreCase` 或仓库现有 ordinal helper。具体受管语法以当前 `OrdinalComparisonContract.ps1` 和测试为准。

## 6. 跨平台验证

需要声明 macOS/Linux/Windows 支持时：

1. 先读 `pwsh scripts/check-script-compatibility.ps1 -?` 获取当前参数和层次；
2. 在目标 OS 上执行 producer 当前要求的 fast/core 验证；
3. 保存 OS、PowerShell/.NET/Docker 等实际版本、命令、退出码和日志；
4. fast-only 或 Windows smoke 只能按 producer 定义的证明范围报告；
5. 历史兼容报告不能替代当前 head 的实测。

## 7. Evidence 与秘密

- 使用脚本自产生的 artifact/evidence，不手写一份“看起来等价”的结果。
- evidence 只证明本次 commit/run/session/provider/lane。
- password、token、secret、完整 connection string、Authorization header、客户密钥不得进入 committed 文件或 retained artifact。
- artifact 采集出现 unavailable 时按 producer 语义记录，不能为了“采证完整”改变被测 lane 的 verdict。

## 8. 停止条件

遇到以下任一情况停止执行并先修复边界：

- 无法确定目标环境或资源所有权；
- 当前脚本帮助与文档冲突；
- cleanup 只能通过广泛 kill/prune/drop 完成；
- 必须把秘密写入日志/manifest 才能继续；
- 需要连接客户/生产数据但入口不是受治理 release-install；
- 需要改变 checker、CI routing 或共享 helper 语义，但当前 Issue 不拥有该规则；
- 失败原因被上层包装吞掉，现有日志不足以区分 signal/timeout/transport/assertion。

需要长期保留的事故/审计结论写入 `docs/reports/`；运行 artifact 本身不提交仓库。