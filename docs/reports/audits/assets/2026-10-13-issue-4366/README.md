# #4366 连续旅程与 R1 独立计价补证

本目录是冻结报告的两份**独立会话**证据与消费脚本，不是新测试框架/CI gate。`evidence.json` 与 `verify-evidence.py` 只归属 `9aa493c14028ba5984f4f61da7b47525406d5c63` / `nerv-0d43-e0363f`；`r4-inventory-public-evidence.json` 与 `verify-r4-inventory-public.py` 只归属 `f2d66acbcba3b735f5cee14f48c7b02f92a4f743` / `nerv-0d43-b8f062`。同名工单序号也不能跨会话合并为同一强 ID。

## 原数据和失败保留

完整归档 `/Users/mang/.t3/archives/Nerv-IIP/issue-4366-20261013/run-nerv-0d43-e0363f-continuous-r3/`，188文件，bundle SHA256 `ec4d61d18f510ff01837ae20813419818e5e2d6361f11220c1553f28c32ed5b4` 不变。evidence.json/sourceFiles保留原公开读回、角色、895条真实UI网络、清理、完整verification.log及25/1结果；sourceFileHashes校验原字节。original-verifier.py逐字节保留并验证原hash。accounting保留从同tar解码的实际ERP/Inventory SQL及关联公开数据，不补造HTTP回执。完整服务日志与26截图在原归档；这里七截图是原字节选集。

## 修正来源与复核

[批准母spec](https://github.com/Mang-X/Nerv-IIP/issues/4288)、[获批切片](https://github.com/Mang-X/Nerv-IIP/issues/4288#issuecomment-6123446945)、[#4366](https://github.com/Mang-X/Nerv-IIP/issues/4366)要求UI同WO/OP/lot、公开成本库存恢复及真实事件关联，不要求零差额。[既有产品语义](../../../../product/erp/design.md)定义累计减资本化为差额。原失败谓词无规格来源，完整保留，不改容差换绿。

修正版本保留原25个事实，另核八关系：累计=资本化+差异；实际凭证sourceNo=公共movement；借=贷；成品借=公开单位成本×量；5101借=公开差异=OP80机器；WIP贷/聚合清理=累计；实际Inventory同lot/量/计价；末机器先于最终收货会计写入。这些是既有会计语义与批准关联标准的关系校验，不定义全局零差异。

```sh
python3 docs/reports/audits/assets/2026-10-13-issue-4366/verify-evidence.py --output artifacts/issue-4366-accounting-calibration/corrected-result.json
```

实际exit0、33成立0失败，完整输出和结果为corrected-verification.log/corrected-result.json。仅归档核验，非新runtime或仓库gate。whole4366Accepted=false、independentReviewRequired=true，独立审核裁定充分性。

## 未验证

公开voucher详情/GL余额、独立DB SELECT/提交审计未捕获；SQL不是公开readback，工单聚合未清成本0不是总账余额0。material-lot/NCR其他支线/PDA/硬件/跨午夜、标准差异unavailable、专门缺费率deadletter replay保持主报告边界。七截图覆盖MPS发布、物料Ready、NCR关闭、真实OP80未完、另一主体接班前/后及同lot成品入库。

## 错误归档反例

[合成变异结果](mutation-results.json)：仅将实际SQL差异借方改0.859138（错金额），或将凭证sourceNo改wrong-movement（错强ID）；修正consumer分别自然exit1，失败在借贷/差异匹配、同movement关联。临时目录结束即删除。这是核验脚本的合成输入反例，不是新运行，也不重新定义业务规则。

## R1 同批公开 Inventory 量价补证

[R1 S1/E1](https://github.com/Mang-X/Nerv-IIP/pull/4397#pullrequestreview-5499398733) 依据 [#4366](https://github.com/Mang-X/Nerv-IIP/issues/4366)、[#4288 spec r1](https://github.com/Mang-X/Nerv-IIP/issues/4288) 和[原操作文档 §7.6](https://github.com/Mang-X/Nerv-IIP/blob/51a408a9c3a323a868d1cd6b31dbd36b239c99e2/docs/reports/audits/2026-10-10-mes-aps-ui-ux-operations.md#L81)要求 Inventory 自身的数量与单位成本终态。旧 9aa 原始归档没有最终过账后的该公开 GET，已删除的会话不能补造。新会话从空 MPS 开场，真实 UI 做完必要 MPS→MRP→采纳→齐套→8 工序/检验→完工登记→ERP 停服恢复，原始 `GET /inventory/movements` 返回同 FGR/WO/OP80 lot 的 qty2、`unitCost=7.415347`、`movementAmount=14.830694`；金额与公开 ERP 资本化及 FGR 一致。[主报告 R1 节](../../issue-4366-continuous-production-2026-10-13.md#r1-补证独立-ui-开场至同批-inventory-公开计价)逐阶段列强 ID、correlation 和证据边界。

[新会话原始选集](r4-inventory-public-evidence.json)嵌入 32 个归档源文件的原字节及 SHA256、72 条实际 UI 写、3 条关键 UI 公开 GET、11 条 API 公开 GET、5 条相关服务事件/SQL日志和八张截图的 hash。完整原始证据包在 `/Users/mang/.t3/archives/Nerv-IIP/issue-4366-20261013/run-nerv-0d43-b8f062-inventory-r4/`，135 文件，tar SHA256 `e933379c9496b687b817207a0ff5177c9207cefb925ba4a5540998ea4fe23fa3`，成员字节核验0异常。四张原字节页面选集：[MPS 输入](r4-01-mps-created-ui.png)、[OP80 完工](r4-19-op80-completed-ui.png)、[FGR Posted](r4-23-fgr-posted-ui.png)、[Inventory 同批流水](r4-23-inventory-movement-ui.png)。页面仅显示数量；公开 GET 的量价字段在选集内。

```sh
python3 docs/reports/audits/assets/2026-10-13-issue-4366/verify-r4-inventory-public.py --output artifacts/issue-4366-r4-inventory-public/r4-verification-result.json
```

实际 exit0、51 PASS/0 FAIL，其中32项校验源文件原字节 hash、19项核 UI 开场、强 ID、公开前后、单位成本×数量=movement金额=ERP资本化、累计=资本化+公开差额、归档与清理。五个错误变异分别改最终单位成本、金额、lot、WO lineId、数量，更新合成源文件 hash 后各 exit1，失败于对应业务关系；结果在新会话完整归档内。这里是新会话事实核验，不将原 25/1 更改或将两会话接成同一执行。新会话未跑 NCR 处置/双主体交接，原 9aa 全链对这些的证据仍独立归属；`whole4366Accepted=false`，交独立审核判断补证充分性。完整 voucher 公开详情、GL 余额和独立 DB SELECT 未取得，不凭空加为本票强制门槛。
