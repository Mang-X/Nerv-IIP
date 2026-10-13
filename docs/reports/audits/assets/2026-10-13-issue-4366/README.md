# #4366 单次连续旅程归档证据

本目录是冻结报告的单次证据与消费脚本，不是新测试框架/CI gate。runtime只归属9aa493c14028ba5984f4f61da7b47525406d5c63/session nerv-0d43-e0363f。

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
