# GS1 解析库兼容与维护收益 Spike（2026-10-07）

## 结论与证明范围

本次对后端与前端分别给出 **no-go：保留现有解析器**。GS1 官方 Syntax Engine `1.4.1` 是可复核的成熟候选，且有真实 npm 分发；阻碍迁移的是两面既有容错语义与部署成本，不能归因为“没有官方库”。本结论只决定本次是否值得换库，不永久排除未来标准语法需求。

- 任务：[Issue #4170 获批规格 2026-10-07-r2](https://github.com/Mang-X/Nerv-IIP/issues/4170)。范围为 M 级限界 Spike；不交付生产迁移、DTO、数据库或业务扩展。
- 仓库调查基线：`950556841fef886cfc39e627336f0bd082658b34`，调查日期 2026-10-07。本报告完成后冻结，不作为当前支持矩阵或治理规则。
- 实际环境：Darwin arm64、Apple clang `21.0.0`、.NET SDK `10.0.302`、Node `v24.18.0`、workspace pnpm `11.22.0`、Vitest `4.1.10`。
- 已执行：原后端源码、原 `parseGs1` 与官方 C# native / release WASM / npm WASM 的 50 个隔离输入观察；现有领域、打印/查询/事件消费者、前端 parser 与 PDA 收货组件测试。没有把桌面 Node 实验声称为 PDA 实机验证。
- [#418](https://github.com/Mang-X/Nerv-IIP/issues/418)、[#556](https://github.com/Mang-X/Nerv-IIP/issues/556) 回读均为 `CLOSED / COMPLETED`；它们已经交付的 GS1/SSCC/EPCIS 业务未重开。

## 两个解析面与责任边界

所有仓库链接固定到调查基线；它们说明现态，只有获批规格、既有明确测试与正式标准能定义预期。没有把偶然接受行为录制成永久 golden。

| 面 | 公开结果与接受边界 | 消费者与最高恰当验证 seam |
| --- | --- | --- |
| 后端 | [`Gs1ApplicationIdentifierParser.Parse`](https://github.com/Mang-X/Nerv-IIP/blob/950556841fef886cfc39e627336f0bd082658b34/backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/BarcodeRuleAggregate/Gs1BarcodeValue.cs#L158) 返回 `Gs1BarcodeValue(Gtin, LotNo, SerialNumber, Quantity, CompanyPrefixLength, Sscc)`；须有 01 或 00；括号格式识别 00/01/10/21/30，未知忽略；raw 跳过 11/17 和任意数字 310x 的 6 字符，其它未知按两位入口跳到下一个 GS；重复字段最后值覆盖；固定值短则抛 `ArgumentException`，括号长则截取；GTIN 内容/校验位不验证；AI30 用 invariant decimal，坏值变 null | [`ScanRecord`](https://github.com/Mang-X/Nerv-IIP/blob/950556841fef886cfc39e627336f0bd082658b34/backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/ScanRecordAggregate/ScanRecord.cs#L238) 仅在 `(01)/(00)` 或有数字身份的 raw 前缀进入解析，提取追溯身份；[`LabelBarcodePayloadFactory`](https://github.com/Mang-X/Nerv-IIP/blob/950556841fef886cfc39e627336f0bd082658b34/backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/LabelBarcodePayloadFactory.cs) 构造 GS1 打印 payload；[`ResolveBarcodeQuery`](https://github.com/Mang-X/Nerv-IIP/blob/950556841fef886cfc39e627336f0bd082658b34/backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/Resolutions/ResolveBarcodeQuery.cs#L115) 结合最大长度、GTIN prefix 判断规则，捕获 `ArgumentException`。使用现有 AggregateTests 与 Web 消费者测试，不复制 parser |
| 前端 | [`parseGs1`](https://github.com/Mang-X/Nerv-IIP/blob/950556841fef886cfc39e627336f0bd082658b34/frontend/packages/business-core/src/barcode/gs1.ts) 同步返回 `Gs1Fields` 或 null；提取 01/11/17/10/21；已知定长 AI 跳过也算 matched（可能只返回 raw）；未知长度停止，保留已识别字段；不支持括号格式。去任意三字符 `]` 前缀和前导 GS；保留去装饰后的 raw；YY→2000+YY，DD=00→当月末，坏真实日期不写入日期字段；变长无 GS 则吃到尾部 | [`inbound.vue`](https://github.com/Mang-X/Nerv-IIP/blob/950556841fef886cfc39e627336f0bd082658b34/frontend/apps/business-pda/src/pages/wms/inbound.vue#L284) 只接受存在 lotNo 或 expiryDate 的结果；当前选中/单行优先，其次按 lotNo 匹配；不匹配提示手输；仅 capture lotNo/productionDate/expiryDate，GTIN/serial 不驱动选行。既有 [`inbound.test.ts`](https://github.com/Mang-X/Nerv-IIP/blob/950556841fef886cfc39e627336f0bd082658b34/frontend/apps/business-pda/src/pages/wms/inbound.test.ts#L544) 验证选行、扫码、completeInbound 的行采集 |

保留在领域/业务一侧的责任：Mod10 与 SSCC 生成、FNC1 插入、明确的 6–12 company prefix length、SGTIN/EPCIS/序列规则、ZPL payload 与扫码入口筛选。Syntax Engine 只能接管标准 AI 字典、分词、长度与内容校验，不拥有这些业务政策。

**AI30 不进入库存动作数量。** `ScanRecord.ParseGs1ValueIfPresent` 不赋值 `Quantity`，库存动作仍要求请求数量正数；`Accepted_inventory_scan_does_not_use_gs1_ai30_as_movement_quantity` 已执行。前端没有 quantity 字段，收货扫码也不 capture quantity。任何迁移必须保持这个边界。

## 候选固定版本、分发与运行负担

| 核查项 | 固定证据与结果 |
| --- | --- |
| 版本 / 上游 | [GS1 Syntax Engine release 1.4.1](https://github.com/gs1/gs1-syntax-engine/releases/tag/1.4.1)，发布时间 `2026-05-28T15:20:45Z`；tag commit `ec595ff68364dd0056abd3b89d087ff5fbc9bf8e`。调查时 main 为 `70c1b162812ef8964cf8d4f28507a1b699cb934f`（2026-08-29）；实验只用固定 release，不混用 main 实现 |
| 成熟度 | GS1 AISBL 的 [Barcode Syntax Resource 参考实现](https://github.com/gs1/gs1-syntax-engine/blob/ec595ff68364dd0056abd3b89d087ff5fbc9bf8e/README.md)，有多语言绑定；固定版 [上游 workflow](https://github.com/gs1/gs1-syntax-engine/blob/ec595ff68364dd0056abd3b89d087ff5fbc9bf8e/.github/workflows/gs1encoders.yml) 覆盖 C/字典、sanitizer、Linux/Windows/macOS。这里只核查接线，不声称本次重跑上游完整测试或确认其每个平台绿色 |
| 许可 | 固定版 [LICENSE](https://github.com/gs1/gs1-syntax-engine/blob/ec595ff68364dd0056abd3b89d087ff5fbc9bf8e/LICENSE) 为 Apache-2.0；npm 包也含 LICENSE。若日后再分发，按原文保留许可与适用 notices、标注修改；本票不向生产制品引入它 |
| 字典更新 | 固定 [Syntax Dictionary](https://github.com/gs1/gs1-syntax-engine/blob/ec595ff68364dd0056abd3b89d087ff5fbc9bf8e/src/c-lib/gs1-syntax-dictionary.txt) 标头 `Release: 2026-01-27`；默认 init 使用编译内嵌 AI table。可显式加载外部字典；失败是否 fallback 是调用方选项。维护字典与 core linter 的上游版本仍须一起复核，不从网络隐式追最新。实验只用内嵌表 |
| .NET 分发 | Release 提供 Windows dotnet-lib 与 x86/x64 native libs；C# [wrapper](https://github.com/gs1/gs1-syntax-engine/blob/ec595ff68364dd0056abd3b89d087ff5fbc9bf8e/src/dotnet-lib/GS1Encoder.cs) P/Invoke 名为 `gs1encoders.dll`，实现 `IDisposable`。固定 csproj 为 net8.0，非 Windows staging 指向 `libgs1encoders.so`，没有 macOS dylib staging 分支。本次以 net10.0 编译 wrapper，并通过 `NativeLibrary.SetDllImportResolver` 加载自编译 dylib。不能把 Windows zip 当作 Nerv Linux 镜像的 RID 完整 NuGet drop-in |
| .NET 目标边界 | 当前 [通用 service Dockerfile](https://github.com/Mang-X/Nerv-IIP/blob/950556841fef886cfc39e627336f0bd082658b34/infra/docker/dotnet-service.Dockerfile) 使用 Linux `aspnet:10.0`、非 root 运行；没有候选 native build/copy。后续若 go，必须明确部署 OS/arch，交付 native 构建/加载/发布资产、资源释放和缺库失败证明。此次只证明 macOS arm64，不证明 Linux 镜像 |
| 浏览器/npm 分发 | [`gs1encoder@1.4.1` registry metadata](https://registry.npmjs.org/gs1encoder/1.4.1) 与 tarball 实查存在；含入口 mjs、TypeScript declarations、WASM loader 与 wasm。源码 package.json 为 1.4.0，而发布包为 1.4.1；以 registry tarball 固定。tarball SHA256 `cacf2441b78ee9013eb67a20acaaef3aa6038b2a08d5e2b5fbaf7812011d2332`，压缩 82,440 bytes / unpacked 263,406 bytes |
| 浏览器加载与体积 | npm 的 wrapper 36,227 bytes、loader 76,638 bytes、wasm 114,489 bytes：运行输入合计 **227,354 bytes**（未压缩，非 Nerv 最终 bundle 增量）。`await GS1encoder.create()` 异步初始化，实例须 free；浏览器需要可寻址 wasm 与正确 MIME。release zip 86,246 bytes，三项运行输入 228,684 bytes；zip 与 npm 的 loader/wasm 字节不同，wrapper 相同，50 向量行为分别验证一致 |
| PDA / 离线 | 当前 [Capacitor 边界](../../architecture/mobile/capacitor.md) 是 Android WebView/Capacitor。同步 parseGs1 若换库，需要启动 ready 生命周期或异步消费者改造、Vite/Capacitor wasm 资产复制、离线加载和错误到手输提示映射。Node 从本地资产加载成功不证明 WebView/APK 离线。release 还有 asm.js 变体，但未评估；它仍是异步 wrapper，也不消除语义适配成本 |

[GS1 官方 User Guide](https://ref.gs1.org/tools/gs1-barcode-syntax-resource/user-guide/) 说明 Syntax Dictionary 与 linter 的职责。下面标准类别的具体预期优先回到固定字典：AI01 `N14,csum`，AI30 `N..8`，11/17 `N6,yymmd0`，3100–3105 `N6`；不能用“标准更严格”自动批准改变历史标签接受政策。

## 兼容矩阵

输入在 [vectors.json](gs1-parser-library-evaluation-2026-10-07.vectors.json)，逐例原始输出在 [observations.jsonl](gs1-parser-library-evaluation-2026-10-07.observations.jsonl)。JSON 的 `\u001d` 是 GS，非可打印文字。`backend/frontend` 是原实现观察；`candidate/relaxed` 分别是默认官方配置和 `PermitUnknownAIs=true + RequisiteAIs=false`。三种候选载体的观察完全相同。

分类：**B**=获批业务/公开合同（Issue r2、既有明确测试）；**S**=固定版标准语法/字典；**L**=legacy observation，只用于发现差异，未自动批准成永久规则。合并分类表示必须由业务 owner 决定是否保持兼容，不能由 parser 替 owner 裁决。

表中“一致”只指可观察字段或均不提取字段；候选返回的 AI 串还须投影成现有结果，候选 exception 也须映射为现有 `ArgumentException` / null。**没有完成一个可部署生产 adapter**。候选不规范化 YYYY-MM-DD、raw、company prefix 或库存数量；这些均留在原层。下面已执行的观察没有“未验证”空格；目标运行/部署与缺资产失败明确列在末节。

| 输入 ID（完整值见 vectors） | 来源与判断点 | 后端原输出 | 前端原输出（省略 raw） | 候选默认；放宽差别 | 对照与影响 |
| --- | --- | --- | --- | --- | --- |
| `bracket-core` | B/S | gtin=09506000134352, lot=LOT-A, serial=SN1, quantity=2 | null | 接受 | 01/10/21/30 标准分词；后端一致，前端差异：原本不接受括号 |
| `raw-core` | B/S | gtin=09506000134352, lot=LOT-A, serial=SN1, quantity=2 | gtin=09506000134352, lotNo=LOT-A, serialNo=SN1 | 接受 | 后端一致；前端投影已有字段一致，仍在 30 停止 |
| `bracket-sscc` | B/S | sscc=123456789012345675 | null | 接受 | 后端一致；前端差异：原本不接受括号 |
| `raw-sscc` | B/S | sscc=123456789012345675 | 仅 raw | 接受 | 后端一致；前端可跳过 00，只返回 raw，须保留 matched 语义 |
| `bracket-generated-gs` | B/S | gtin=09506000134352, lot=LOT-A, serial=SN1, quantity=2 | null | 拒绝 | 差异：既有 ToAiString 插入 GS，候选将其当字符集错误；需额外规范化 |
| `raw-date-weight` | B/S | gtin=09506000134352, lot=LOT, serial=SN | gtin=09506000134352, productionDate=2024-01-01, expiryDate=2026-12-31 | 接受 | 后端投影一致；前端差异：遇 3103 即停，候选继续获取 lot/serial |
| `raw-3109` | L/S | gtin=09506000134352, lot=LOT | gtin=09506000134352 | 拒绝；放宽接受 | 差异：后端按任意数字 310x 跳过；候选默认未知，放宽后按已知 prefix 长度恢复 |
| `unknown-2-start` | L | gtin=09506000134352, lot=LOT | null | 拒绝 | 差异：后端跨 GS 跳过继续；前端开头 null / 中间保留 GTIN / 尾部保留 GTIN+LOT；候选默认与放宽均整串拒绝 |
| `unknown-2-middle` | L | gtin=09506000134352, lot=LOT | gtin=09506000134352 | 拒绝 | 差异：后端跨 GS 跳过继续；前端开头 null / 中间保留 GTIN / 尾部保留 GTIN+LOT；候选默认与放宽均整串拒绝 |
| `unknown-2-tail` | L | gtin=09506000134352, lot=LOT | gtin=09506000134352, lotNo=LOT | 拒绝 | 差异：后端跨 GS 跳过继续；前端开头 null / 中间保留 GTIN / 尾部保留 GTIN+LOT；候选默认与放宽均整串拒绝 |
| `unknown-3-start` | L | gtin=09506000134352, lot=LOT | null | 拒绝 | 差异：后端跨 GS 跳过继续；前端开头 null / 中间保留 GTIN / 尾部保留 GTIN+LOT；候选默认与放宽均整串拒绝 |
| `unknown-3-middle` | L | gtin=09506000134352, lot=LOT | gtin=09506000134352 | 拒绝 | 差异：后端跨 GS 跳过继续；前端开头 null / 中间保留 GTIN / 尾部保留 GTIN+LOT；候选默认与放宽均整串拒绝 |
| `unknown-3-tail` | L | gtin=09506000134352, lot=LOT | gtin=09506000134352, lotNo=LOT | 拒绝 | 差异：后端跨 GS 跳过继续；前端开头 null / 中间保留 GTIN / 尾部保留 GTIN+LOT；候选默认与放宽均整串拒绝 |
| `unknown-4-start` | L | gtin=09506000134352, lot=LOT | null | 拒绝 | 差异：后端跨 GS 跳过继续；前端开头 null / 中间保留 GTIN / 尾部保留 GTIN+LOT；候选默认与放宽均整串拒绝 |
| `unknown-4-middle` | L | gtin=09506000134352, lot=LOT | gtin=09506000134352 | 拒绝 | 差异：后端跨 GS 跳过继续；前端开头 null / 中间保留 GTIN / 尾部保留 GTIN+LOT；候选默认与放宽均整串拒绝 |
| `unknown-4-tail` | L | gtin=09506000134352, lot=LOT | gtin=09506000134352, lotNo=LOT | 拒绝 | 差异：后端跨 GS 跳过继续；前端开头 null / 中间保留 GTIN / 尾部保留 GTIN+LOT；候选默认与放宽均整串拒绝 |
| `bracket-unknown` | L | gtin=09506000134352, lot=LOT | null | 拒绝；放宽接受 | 差异：后端忽略 888；候选默认拒绝，放宽可接受；前端仍不接受括号 |
| `missing-separator` | B/S | gtin=09506000134352, lot=LOT21SN | gtin=09506000134352, lotNo=LOT21SN | 接受 | 一致：无法推断 21，LOT21SN 全部作为批号；不凭内容猜分隔 |
| `duplicate-lot` | L/S | gtin=09506000134352, lot=LAST | gtin=09506000134352, lotNo=LAST | 拒绝 | 差异：原后/前端最后值 LAST；候选拒绝冲突重复值 |
| `duplicate-gtin` | L/S | gtin=09521234543213 | null | 拒绝 | 差异：两个有效 GTIN 原后端取最后值；候选拒绝冲突重复值 |
| `short-fixed-bracket` | L/S | 拒绝 | null | 拒绝 | 后端一致：拒绝短值；前端无字段，仍须映射候选错误模型 |
| `short-fixed-raw` | L/S | 拒绝 | null | 拒绝 | 后端一致：拒绝短值；前端 null，仍须映射候选错误模型 |
| `long-fixed-bracket` | L/S | gtin=09506000134352, lot=LOT | null | 拒绝 | 差异：后端截到 14；候选拒绝长值 |
| `long-fixed-raw` | L/S | gtin=09506000134352 | gtin=09506000134352 | 接受 | 投影身份一致但分词差异：多出 9 与后续 10 合成已知 AI91，候选保留 (91)0LOT；不得声称整串为非法长 01 |
| `truncated-ai` | L/S | 拒绝 | gtin=09506000134352 | 拒绝 | 后端一致：拒绝；前端差异：保留 GTIN |
| `truncated-bracket` | L/S | 拒绝 | null | 拒绝 | 后端一致：拒绝；前端无字段，仍须映射错误模型 |
| `truncated-date` | L/S | 拒绝 | gtin=09506000134352 | 拒绝 | 后端一致：拒绝；前端差异：保留 GTIN |
| `lot-only` | B/S | 拒绝 | lotNo=LOT | 拒绝；放宽接受 | 差异：后端缺身份拒绝；前端仍可采批号；候选须关闭 requisite 才接受，后端还须单独保留身份 guard |
| `no-identity-date` | B/S | 拒绝 | expiryDate=2026-12-31, lotNo=LOT | 拒绝；放宽接受 | 差异：PDA 批号/效期不要求 GTIN；候选默认 requisite 拒绝，放宽恢复 |
| `nondigit-gtin` | L/S | gtin=ABCDEFGHIJKLMN, lot=LOT | null | 拒绝 | 差异：后端无数字验证，候选拒绝；不是本票授权的校验政策升级 |
| `nondigit-gtin-raw` | L/S | gtin=ABCDEFGHIJKLMN, lot=LOT | gtin=ABCDEFGHIJKLMN, lotNo=LOT | 拒绝 | 差异：原 parser 均提取；ScanRecord raw 入口另有数字筛选，不能混为同一合同 |
| `bad-check-digit` | L/S | gtin=09506000134353, lot=LOT | gtin=09506000134353, lotNo=LOT | 拒绝 | 差异：原 parser 均提取；候选拒绝校验位 |
| `invalid-month` | B/S | gtin=09506000134352, lot=LOT | gtin=09506000134352, lotNo=LOT | 拒绝 | 差异：后端跳过日期、前端不填坏日期但保留 LOT；候选整串拒绝 |
| `invalid-day` | B/S | gtin=09506000134352, lot=LOT | gtin=09506000134352, lotNo=LOT | 拒绝 | 差异：原后端保留 LOT、前端不填 2/31 但保留 LOT；候选整串拒绝 |
| `leap` | B/S | gtin=09506000134352, lot=LOT | gtin=09506000134352, expiryDate=2024-02-29, lotNo=LOT | 接受 | 合法 2024-02-29，一致；前端日期投影仍归原层 |
| `non-leap` | B/S | gtin=09506000134352, lot=LOT | gtin=09506000134352, lotNo=LOT | 拒绝 | 差异：前端不填 2025-02-29 但保留 LOT；候选整串拒绝 |
| `month-end` | B/S | gtin=09506000134352, lot=LOT | gtin=09506000134352, expiryDate=2026-02-28, lotNo=LOT | 接受 | 一致：允许 DD00；前端映射 2026-02-28，候选保留 260200 |
| `leap-month-end` | B/S | gtin=09506000134352, lot=LOT | gtin=09506000134352, expiryDate=2024-02-29, lotNo=LOT | 接受 | 一致：允许 DD00；前端映射 2024-02-29，候选保留 240200 |
| `century` | B | gtin=09506000134352, lot=LOT | gtin=09506000134352, expiryDate=2099-01-01, lotNo=LOT | 接受 | 候选只保留 990101；2099 年投影仍须保持 YY→2000+YY，未由候选替代 |
| `production-date` | B/S | gtin=09506000134352, lot=LOT | gtin=09506000134352, productionDate=2026-07-01, lotNo=LOT | 接受 | 后端跳过、前端 2026-07-01；标准分词一致，投影仍归原层 |
| `symbology-c1` | B/L | 拒绝 | gtin=09506000134352, lotNo=LOT | 接受 | 前端一致；后端差异：原 parser 不去 ]C1，ScanRecord 原入口也不进入 |
| `symbology-d2` | B/L | 拒绝 | gtin=09506000134352, lotNo=LOT | 接受 | 前端一致；后端差异：原 parser 不去 ]d2，ScanRecord 原入口也不进入 |
| `leading-gs` | B | gtin=09506000134352, lot=LOT | gtin=09506000134352, lotNo=LOT | 接受 | 字段一致；实验输入 adapter 将前导 GS 改成候选前置 FNC1 |
| `empty` | B/S | 拒绝 | null | 拒绝 | 均无字段；后端抛错/前端 null 仍需各自错误映射 |
| `quantity-decimal` | L/S | gtin=09506000134352, quantity=2.5 | gtin=09506000134352 | 拒绝 | 差异：原后端 decimal=2.5，标准 AI30 只允许数字；前端停在 30 |
| `quantity-invalid` | L/S | gtin=09506000134352 | gtin=09506000134352 | 拒绝 | 差异：原后端 Quantity=null 但保留 GTIN，候选整串拒绝 |
| `quantity-negative` | L/S | gtin=09506000134352, quantity=-2 | null | 拒绝 | 差异：原后端 decimal=-2，候选拒绝；这不代表允许负库存动作请求 |
| `quantity-too-long` | L/S | gtin=09506000134352, quantity=123456789 | gtin=09506000134352 | 拒绝 | 差异：原后端 decimal=123456789，候选超过 N..8 拒绝 |
| `empty-variable` | L/S | gtin=09506000134352, serial=SN | gtin=09506000134352 | 拒绝 | 差异：后端 lot=null 后继续 serial=SN，前端保留此前 GTIN，候选整串拒绝 |
| `known-fixed-skip` | B/L/S | gtin=09506000134352 | gtin=09506000134352, expiryDate=2026-12-31, lotNo=LOT | 接受 | 前端字段一致；后端差异：未识别 raw 15 按变长跳到尾部，未得到 LOT；候选能继续 |

`PermitUnknownAIs` **不是通用 raw 未知跳过模式**。固定版 [ai.c](https://github.com/gs1/gs1-syntax-engine/blob/ec595ff68364dd0056abd3b89d087ff5fbc9bf8e/src/c-lib/ai.c#L321) 可由已知 prefix 推导 3/4 位 AI 长度（3109 是实测例），但 [raw 提取](https://github.com/gs1/gs1-syntax-engine/blob/ec595ff68364dd0056abd3b89d087ff5fbc9bf8e/src/c-lib/ai.c#L806) 拒绝未知 AI 长度。89 / 888 / 8888 的 9 种位置输入已分别跑过；不把上游在线 wrapper 文档的概述扩成“任何 raw 未知都不可能”，也不把 3109 特例扩成“所有未知可忽略”。

## 净删除责任与新增成本

计数以原文件物理行数为粗上限，**不把候选总代码减去旧文件称为净收益**。同一业务语义若必须留下，就没有因移交标准字典而删除其维护责任。

| 面 | 最理想可移交 | 必须保留或新增 | 净删除判断 / 决定 |
| --- | --- | --- | --- |
| 后端 | `Gs1BarcodeValue.cs` 总 352 行中的 parser 位于 158–352，即 **195 行上限**；AI 识别/分词/长度可交上游 | 生成与领域值对象前 157 行保留；01/00 身份 guard、quantity decimal 语义、字段投影、现有异常映射仍保留。兼容未知跳过、截长、重复覆盖、坏 AI30 以及日期跳过须在候选前处理或保留 legacy 路径；这重建了原 parser 的主要分词责任。另增 native OS/arch 构建发布、P/Invoke 加载、实例释放、镜像资产与加载失败测试；wrapper 1,006 行由上游维护，不能当作手写 adapter 已被删除 | 在不改变接受政策时，可确认移走的完整责任 **0 项**：仅定长字典维护被上游接管，但 legacy 容错扫描仍要保留，代码净删除没有已证实正值。为仅 5 个提取 AI 与 3 类跳过引入 native 供应链，不成立。**no-go** |
| 前端 | `gs1.ts` **133 行上限**，其中定长表、变长集合与循环是可移交候选；标准支持 AI 更多 | 日期、YY 世纪、月底、raw、前缀、partial/null 模型、unknown stop、同步调用与 PDA 选行/手输仍保留。为保住坏日期后的 LOT 和未知前半结果，不能只 try/catch 全串验证；必须保留逐段扫描或另造预解析。新增异步 ready、实例 free、WASM 资产/MIME/离线打包与失败反馈、npm 版本锁定及 WebView/APK 验证 | 现有业务语义要求的前缀/日期/partial 扫描责任 **0 项可完整删除**；固定 npm 运行输入约 227 KB，实际 app 增量未测，不能用“已有 vite-plugin-wasm”证明无部署成本。**no-go** |

两个面都没有为了“统一”增加共享错误模型或 production adapter。若以后业务需要更大 GS1 标准覆盖，可重新评估；首先由产品/BarcodeLabel owner 明确哪些 L 类接受行为需要兼容、哪些允许拒绝，并由 PDA owner 明确 partial 与手输要求。未裁决的未知、重复、截长、坏 GTIN、AI30 容错为 **迁移 blocker**，本票以 no-go 完成，不为消除 blocker 新增机制或自动开迁移票。

若未来结论转 go，后续必须独立定级，后端和前端可各自迁移：保持现有外部结果、业务数量隔离与生成/EPCIS 边界；附目标 Linux native 或 Android WebView/APK 的构建/加载/离线/缺资产失败证明，复用现有消费者 seam。不能由本报告授权语义改变或生产部署。

## 实际验证与未运行项

原生产与测试源码未修改；以下是本地 baseline 证据，不代替 PR exact-head CI。实验输出是诊断 observations，不是从现状反向批准的断言或 golden；只对 native/WASM/npm 的结果做一致性检查。

| 实际命令 | 实际输出 / 证明范围 |
| --- | --- |
| `make -C /tmp/nerv-4170-gs1-upstream/src/c-lib libshared` | exit 0；macOS arm64 `libgs1encoders.dylib.1.4.1` 294,920 bytes；不证明 Linux 发布 |
| `dotnet run --no-build --project /tmp/nerv-4170-experiment/Experiment.csproj -- /tmp/nerv-4170-experiment/vectors.json` | 50 条 .NET 原 parser 与官方 wrapper 结果，native 使用自定义 resolver；读取固定内嵌表 |
| `node --experimental-strip-types /tmp/nerv-4170-experiment/run.mjs` | `50 vectors: native C# / WASM agree; 16 default accepted; 20 relaxed accepted`；本地 release WASM 加载 |
| `node --experimental-strip-types /tmp/nerv-4170-experiment/run-npm.mjs` 后 `cmp .../results.jsonl .../npm-results.jsonl` | 同一 50/16/20 输出；cmp exit 0。不同分发字节的两种 WASM 不因此被当作同一个 artifact |
| `dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj --filter FullyQualifiedName~BarcodeLabelAggregateTests --logger 'trx;LogFileName=issue-4170-baseline.trx' --results-directory /tmp/nerv-4170-test-results` | 42 passed，0 failed，0 skipped；含 GTIN/SSCC/FNC1/SGTIN 和 AI30 业务数量隔离 |
| `dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj --filter 'FullyQualifiedName~CreateLabelPrintBatchCommandTests\|FullyQualifiedName~BarcodeLabelListQueryTests\|FullyQualifiedName~BarcodeLabelIntegrationEventTests' --logger 'trx;LogFileName=issue-4170-consumers.trx' --results-directory /tmp/nerv-4170-test-results` | 39 passed，0 failed，0 skipped；打印 payload、查询及事件消费者；使用现有测试 provider，不声明真实 PostgreSQL |
| `pnpm -C frontend/packages/business-core exec vitest run src/barcode/gs1.test.ts` | 11 passed / 1 file；公开 parser 日期/前缀/容错 seam |
| `pnpm -C frontend/apps/business-pda exec vitest run src/pages/wms/inbound.test.ts` | 26 passed / 1 file；PDA 收货组件与 mock API 行采集；输出包含 `Could not parse CSS stylesheet`，未使测试失败，不声称真栈或真机 |

未运行：候选 Linux x64/arm64 service 镜像构建/发布/缺 native 失败、浏览器 Vite 最终 bundle 与 WASM MIME/缺文件/初始化失败、Android WebView/APK 离线/实体扫码枪、全栈、真实 PostgreSQL/Redis-CAP、上游完整测试、asm.js 变体、性能和真实 bundle 压缩增量。没有 go，因此不为本票扩展为多平台移植。CI 与合并信息留在 PR，不复制为报告的长期状态。

## 隔离实验复现

下列为本次实际使用的短实验源码；路径对应 `/tmp/nerv-4170-*`。其它机器替换仓库绝对路径与 native suffix/resolver，保持固定 SHA、输入和候选配置。实验目录不进入生产项目，不需要给仓库增加依赖。失败输入以结构化错误记录，候选初始化错误在 try 外直接失败，没有吞掉加载失败。

准备：

```sh
git clone --depth 1 --branch 1.4.1 https://github.com/gs1/gs1-syntax-engine.git /tmp/nerv-4170-gs1-upstream
git -C /tmp/nerv-4170-gs1-upstream rev-parse HEAD
make -C /tmp/nerv-4170-gs1-upstream/src/c-lib libshared
gh release download 1.4.1 --repo gs1/gs1-syntax-engine --pattern gs1encoders-wasm-app.zip --dir /tmp/nerv-4170-gs1-assets
unzip -q /tmp/nerv-4170-gs1-assets/gs1encoders-wasm-app.zip -d /tmp/nerv-4170-gs1-assets/wasm
npm pack gs1encoder@1.4.1 --pack-destination /tmp/nerv-4170-gs1-assets --json
mkdir -p /tmp/nerv-4170-gs1-assets/npm /tmp/nerv-4170-experiment
tar -xzf /tmp/nerv-4170-gs1-assets/gs1encoder-1.4.1.tgz -C /tmp/nerv-4170-gs1-assets/npm
cp docs/reports/spikes/gs1-parser-library-evaluation-2026-10-07.vectors.json /tmp/nerv-4170-experiment/vectors.json
```

`Experiment.csproj`（直接编译原源码，未复制 parser 实现）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <Compile Include="/Users/mang/.t3/worktrees/Nerv-IIP/issue-4170-gs1-spike/backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/BarcodeRuleAggregate/Gs1BarcodeValue.cs" />
    <Compile Include="/Users/mang/.t3/worktrees/Nerv-IIP/issue-4170-gs1-spike/backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/BarcodeLabelText.cs" />
    <Compile Include="/tmp/nerv-4170-gs1-upstream/src/dotnet-lib/GS1Encoder.cs" />
  </ItemGroup>
</Project>
```

`Program.cs`：

```csharp
using System.Text.Json;
using System.Runtime.InteropServices;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using GS1.Encoders;
NativeLibrary.SetDllImportResolver(typeof(GS1Encoder).Assembly, (name, asm, path) => name == "gs1encoders.dll" ? NativeLibrary.Load("/tmp/nerv-4170-gs1-upstream/src/c-lib/build/libgs1encoders.dylib") : IntPtr.Zero);
var cases = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(args[0]))!;
foreach (var c in cases) {
    var input = c.GetProperty("input").GetString()!;
    object legacy;
    try { var v = Gs1ApplicationIdentifierParser.Parse(input); legacy = new {ok=true,gtin=v.Gtin,sscc=v.Sscc,lot=v.LotNo,serial=v.SerialNumber,quantity=v.Quantity}; }
    catch(Exception e) { legacy = new {ok=false,error=e.Message.Split('\n')[0]}; }
    object Check(bool relaxed) {
        using var gs = new GS1Encoder();
        if (relaxed) { gs.PermitUnknownAIs = true; gs.SetValidationEnabled(GS1Encoder.Validation.RequisiteAIs, false); }
        try {
            if (input.StartsWith('(')) gs.AIdataStr = input.Trim();
            else if (input.StartsWith(']')) gs.ScanData = input.Trim();
            else gs.DataStr = "^" + input.Trim().TrimStart('\u001D').Replace('\u001D','^');
            return new {ok=true,ai=gs.AIdataStr};
        } catch(Exception e) { return new {ok=false,error=e.Message}; }
    }
    Console.WriteLine(JsonSerializer.Serialize(new {id=c.GetProperty("id").GetString(),legacy,candidate=Check(false),relaxed=Check(true)}));
}
```

`run.mjs`：

```javascript
import {readFileSync,writeFileSync} from 'node:fs'
import {parseGs1} from '/Users/mang/.t3/worktrees/Nerv-IIP/issue-4170-gs1-spike/frontend/packages/business-core/src/barcode/gs1.ts'
import {GS1encoder} from '/tmp/nerv-4170-gs1-assets/wasm/gs1encoder.mjs'
const cases=JSON.parse(readFileSync('/tmp/nerv-4170-experiment/vectors.json','utf8'))
const dotnet=new Map(readFileSync('/tmp/nerv-4170-experiment/dotnet.jsonl','utf8').trim().split('\n').map(s=>{const v=JSON.parse(s);return[v.id,v]}))
const out=[]
for (const c of cases) {
 async function check(relaxed) {
  const gs=await GS1encoder.create()
  if (relaxed) {gs.permitUnknownAIs=true;gs.setValidationEnabled(GS1encoder.validation.RequisiteAIs,false)}
  try {
   if(c.input.startsWith('('))gs.aiDataStr=c.input.trim()
   else if(c.input.startsWith(']'))gs.scanData=c.input.trim()
   else gs.dataStr='^'+c.input.trim().replace(/^\x1d+/,'').replaceAll('\x1d','^')
   return{ok:true,ai:gs.aiDataStr}
  }catch(e){return{ok:false,error:e.message}}
  finally{gs.free()}
 }
 const native=dotnet.get(c.id)
 const wasm=await check(false),wasmRelaxed=await check(true)
 if(JSON.stringify(native.candidate)!==JSON.stringify(wasm)||JSON.stringify(native.relaxed)!==JSON.stringify(wasmRelaxed))throw Error('Native/WASM disagreement: '+c.id)
 out.push({...c,backend:native.legacy,frontend:parseGs1(c.input),candidate:wasm,relaxed:wasmRelaxed})
}
writeFileSync('/tmp/nerv-4170-experiment/results.jsonl',out.map(o=>JSON.stringify(o)).join('\n')+'\n')
console.log(`${out.length} vectors: native C# / WASM agree; ${out.filter(x=>x.candidate.ok).length} default accepted; ${out.filter(x=>x.relaxed.ok).length} relaxed accepted`)
```

运行并比较 npm 分发：

```sh
dotnet build /tmp/nerv-4170-experiment/Experiment.csproj
dotnet run --no-build --project /tmp/nerv-4170-experiment/Experiment.csproj -- /tmp/nerv-4170-experiment/vectors.json > /tmp/nerv-4170-experiment/dotnet.jsonl
node --experimental-strip-types /tmp/nerv-4170-experiment/run.mjs
sed 's|/tmp/nerv-4170-gs1-assets/wasm/gs1encoder.mjs|/tmp/nerv-4170-gs1-assets/npm/package/gs1encoder.mjs|; s|results.jsonl|npm-results.jsonl|' /tmp/nerv-4170-experiment/run.mjs > /tmp/nerv-4170-experiment/run-npm.mjs
node --experimental-strip-types /tmp/nerv-4170-experiment/run-npm.mjs
cmp /tmp/nerv-4170-experiment/results.jsonl /tmp/nerv-4170-experiment/npm-results.jsonl
```

输入 adapter 只做：(a) 括号用 AIdataStr；(b) `]` 前缀用 ScanData；(c) raw 加首位 `^`，将 GS 改为候选 FNC1 记法。未对坏值进行修复，未剥除括号格式中的 GS，未加生产 fallback；故 `bracket-generated-gs` 的差异真实可见。
