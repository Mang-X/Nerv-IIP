# 测试证据治理

本文定义 Nerv-IIP 当前测试证据的失败关闭、来源证明、隐私与证明范围规则。实现事实以 `.github/workflows/ci.yml`、`scripts/test-evidence-policy.json`、`scripts/test-evidence-baseline.json`、`scripts/lib/TestEvidence*.ps1`、采集/验证脚本和对应契约测试为准；本文不维护易漂移的 lane 成员数或某次运行结果。

## 1. 原生测试结果与证据采集

- `dotnet test` / VSTest 的自然退出码是测试结果权威；不得用 `continue-on-error`、shell 管道或状态恢复包装器把失败改成绿色。
- 证据采集与上传可以在失败后执行，但只能观察、脱敏和归档，不能改变测试步骤的成功/失败结论。
- raw TRX、stdout/stderr、请求/响应正文、连接串和未脱敏附件不得作为 retained artifact 上传；保留产物只包含规范化 TRX、结构化测试记录、摘要与有界诊断。
- 失败测试原始消息留在受访问控制的 Actions job log；retained evidence 只保存隐私安全的固定/脱敏表示。

当前 artifact 结构、schema、保留期、文件命名和 redaction 细节由 TestEvidence producer 与测试定义，不能靠本文复制第二份实现规范。

## 2. Lane provenance 与唯一证据 owner

每份 formal evidence 必须能回答：测试了哪个 SHA、哪个 workflow run/attempt、哪个物理 job、哪个稳定 lane、实际执行多少，以及 cleanup/collection 是否完成。

- backend 快速测试的物理分片、Connector Host、PostgreSQL、Redis/CAP 与 FullChain 的实际 job 绑定以 workflow、lane manifest 和 `Get-NervTestEvidenceLaneJobs` 为准。
- 不执行测试的稳定 aggregate 只能汇总结果，不能认证一条从未由它执行的 evidence lane。
- `full-chain` 的正式 evidence owner 是实际运行 v1 场景的物理 worker；planning、shadow、equivalence 和稳定 aggregate 各自只证明其声明的轨道/比较/汇总职责。
- 同一个 collector 调用只拥有一个物理 lane；不能用同级 selector 声称一次调用分别认证多个物理执行者。

具体 producer 导航见 [`../../reference/testing/producers.md`](../../reference/testing/producers.md)。

## 3. skip、quarantine 与 zero-execution

策略分类保留 `optional`、`environment-gated` 与 `quarantined` 等由当前 policy producer 定义的语义。以下情况必须失败关闭：

- `unregistered-skip`：skip 没有唯一合法登记、原因不匹配或上下文不允许；
- `illegal-quarantine`：隔离元数据缺失、无效或已到期；
- `zero-execution`：被选中的真实依赖 lane 没有任何 passed/failed 运行时结果；skipped 不算执行。

未被影响计划选中的 lane 应由汇总明确报告 `skipped by policy`；这与“job 实际运行并通过”不是同一事实。被选中后若依赖变量、readiness、身份或证据缺失，不得退化成绿色 skip。

## 4. 选择、身份与证明边界

- 选择器、manifest、policy 与测试身份必须双向闭合；分片或 lane 不能私自把未登记测试移出默认门禁。
- 运行时测试身份、来源路径、rule/lane 等字符串身份使用明确的 ordinal 语义，具体函数和覆盖面由 TestEvidence/Ordinal producer 及测试定义。
- `selectedLaneResults` 或等价汇总必须区分实际 `success`、实际 `failure/cancelled` 与 policy `skipped`；不得把 contract test 成功冒充对应真实 provider lane 已运行。
- fixture/local 只证明局部合同；PR exact-head 只证明该 head；merge-SHA main 是新的证据边界；nightly/真机又是独立边界。

## 5. 证据隐私

保留证据必须脱敏授权头、token、password、client secret、PEM、connection string 与受控 PII/正文类字段；失败路径也必须走同一隐私策略。禁止为了“更好排障”把 raw request/response、客户正文或秘密复制到 summary、diagnostics 或 committed report。

证据缺失时应报告 unavailable/collection failure，而不是生成看似完整的空摘要。

## 6. CI timeout 与 evidence reachability

Evidence job 的预算必须保证：某个测试/步骤失败后，负责收集和上传 retained evidence 的后续步骤仍有机会执行。具体 job/step timeout 数字以当前 workflow 为准，不在 Governance 冻结。

- 有失败后仍可运行的 evidence/cleanup 步骤时，预算设计必须防止 job 级 timeout 先取消整个作业而让 `always()` 永远不可达。
- 没有失败后续职责的 job 可以使用更紧的 job 预算作为 fail-fast 上限。
- workflow budget 结构由现有脚本和契约测试验证；不得为本文再造第二套 YAML parser 或自然语言门禁。

操作说明见 [`../../runbooks/testing/evidence.md`](../../runbooks/testing/evidence.md)。

## 7. Timing 是缓存，不是治理资产

测试耗时是观测值，不是“应当如此”的政策。timing cache 可以用于分片配平与报告，但条目缺失、缓存过期或数据源不可用不能因为耗时本身阻止合并；结构性 manifest/policy 缺陷仍由相应治理门禁负责。

因此：

- timing 以近期成功证据自动刷新或降级估值；
- `report-only` 指标不得反向成为测试正确性或 lane 合法性的来源；
- 不建立必须人工刷新 timing snapshot 的 gate 或 hash registry。

历史形成过程见 [`../../reports/audits/test-evidence-governance-evolution-2026-08.md`](../../reports/audits/test-evidence-governance-evolution-2026-08.md)。
