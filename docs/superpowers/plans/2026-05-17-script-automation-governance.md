# 脚本自动化治理实施计划

> **面向智能体执行者：** 必须使用子技能：在多个执行者之间拆分本计划时，使用 superpowers:subagent-driven-development 或 superpowers:executing-plans。各步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 将 ADR 0010 转化为可执行的脚本治理基线：文档、共享 PowerShell 辅助库、静态门禁、夹具，以及首个高风险验证脚本迁移。

**架构：** ADR 0010 聚焦于持久决策边界。操作规则放在 `docs/architecture/script-automation-governance.md`。可复用的 PowerShell 执行原语放在 `scripts/lib/ScriptAutomation.ps1`。解析器/AST 检查放在 `scripts/check-script-governance.ps1`，并在迁移现有脚本期间设置显式的遗留豁免。

**技术栈：** PowerShell 7、.NET 10、Docker Compose、pnpm、Git、本地 `artifacts/script-logs/**`。

---

## 完成记录

本计划从提交 `eef40a8 fix: harden iam persistent auth review gaps` 开始，该提交位于分支 `codex/iam-persistent-auth-foundation` 上。

已知交接说明：本计划开始前 `skills-lock.json` 已处于脏状态，先前审核未报告文本差异。除非用户明确要求，否则不得暂存或修改该文件。

## 边界

1. 不得在一次工作中重写所有遗留脚本。
2. 不得将本地 `verify` 脚本转换为客户使用的 `release-install` 脚本。
3. 本次不得添加特定 CI 提供商的文件。
4. 除非脚本迁移暴露真实测试故障，否则不得更改业务代码。
5. 不得暂存无关的 `skills-lock.json` 变更。

## 文件结构图

```text
docs/adr/
  0010-automation-script-trusted-execution-governance.md

docs/architecture/
  script-automation-governance.md
  deployment-baseline.md
  database-release-runbook.md
  implementation-readiness.md
  repo-layout.md
  api-contract-and-codegen.md

scripts/
  lib/ScriptAutomation.ps1
  check-script-governance.ps1
  tests/check-script-governance.Tests.ps1
  tests/fixtures/script-governance/*.ps1
  verify-iam-persistent-auth-foundation.ps1
```

## 任务 1：冻结文档

- [x] 为脚本可信执行治理添加 ADR 0010。
- [x] 添加架构级脚本自动化治理规则和迁移矩阵。
- [x] 在部署、数据库发布、仓库布局、API 生成、实施就绪状态和 README 中交叉引用 ADR 0010。

## 任务 2：添加预期失败的门禁测试

- [x] 为允许的辅助库用法、缺失辅助库、直接调用 `dotnet`、直接调用 `Start-Job` 和动态调用添加夹具脚本。
- [x] 添加本地 PowerShell 测试工具，运行 `scripts/check-script-governance.ps1 -Path <fixture>` 并断言通过/失败场景。
- [x] 在实施门禁前运行该工具，确认它因预期缺失的命令而失败。

## 任务 3：实施共享辅助库和静态门禁

- [x] 添加 `scripts/lib/ScriptAutomation.ps1`，提供带超时的原生命令执行、命令包装器、进程树清理、作用域环境变量和诊断脱敏。
- [x] 添加 `scripts/check-script-governance.ps1`，使用 PowerShell 解析器/AST 检查、必需的治理头、辅助库 dot-source 检测和显式遗留豁免。
- [x] 运行夹具测试和 `pwsh scripts/check-script-governance.ps1`。

## 任务 4：迁移 IAM 验证脚本

- [x] 添加 `Script-Governance` 元数据，目标为 `scripts/verify-iam-persistent-auth-foundation.ps1`。
- [x] 使用辅助包装器替换直接的 `dotnet`、`docker`、`pnpm` 或嵌套 `pwsh` 调用。
- [x] 确保前台原生命令通过辅助库获得超时/PID/日志记录；此 IAM 脚本不直接启动后台服务进程。
- [x] 重新运行 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1`。

## 任务 5：最终验证

- [x] 运行脚本治理测试。
- [x] 运行脚本治理门禁。
- [x] 运行已迁移的 IAM 验证脚本。
- [x] 运行 `git diff --check`。
- [x] 以聚焦的提交提交文档和脚本治理实现，并保持无关的 `skills-lock.json` 不变。

## 后续待办

- [x] 继续将遗留 `verify` 脚本迁移到 `scripts/lib/ScriptAutomation.ps1`，优先处理 `verify-fifth-slice-persistence-foundation.ps1` 和 `verify-fourth-slice-real-infra.ps1`。
- [x] 添加并运行 macOS/Linux 兼容性门禁：至少在非 Windows 环境中运行 `pwsh scripts/check-script-governance.ps1`、`pwsh scripts/tests/check-script-governance.Tests.ps1`、`git diff --check` 和已迁移的核心验证脚本 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1`。

完成说明：`scripts/check-script-compatibility.ps1` 在 `artifacts/script-logs/script-compatibility/**/evidence.json` 下记录兼容性证据；最终兼容性脚本的完整 Ubuntu WSL 证据记录在 `artifacts/script-logs/script-compatibility/20260518-000559-198/evidence.json`。第四/第五阶段验证脚本的优先级基线豁免已移除。其余遗留脚本作为后续迁移工作跟踪，不会阻塞下一功能阶段。
