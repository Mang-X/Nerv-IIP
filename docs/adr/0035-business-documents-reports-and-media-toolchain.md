# ADR 0035：业务文档、报表与媒体处理工具链

- 状态：已接受（转录既有裁决，不代表实现已完成）
- 日期：2026-08-24
- 原始来源：[Linear 业务文档、报表与媒体处理工具链裁决](https://linear.app/mangax/document/业务文档报表与媒体处理工具链裁决-9581e78b4378)，原文更新于 2026-08-24。

## 背景与理由

Office/PDF、报表、发票、标签打印和音视频是五个独立子域。选型标准是现代、活跃、稳定、文档生态完善、适合 AI 作为主要开发者，并支持私有化 Linux 容器部署。

本记录转录原文已接受的边界；仓内长期决策以本 ADR 为住所，原始来源用于追溯。资料与实际依赖 producer 从 [技术栈资料索引](../reference/technology-stack.md) 查询，正式基线不等于当前已经安装或启用。

## 决策

五个子域只共享处理任务、FileStorage 产物引用、模板版本、SHA-256、限额、审计和错误语义，不共享万能文档模型或「文件工具库」。权限、金额、编码与业务事实仍由所属模块负责。

| 子域 | 正式基线 | 可选或暂缓 |
| --- | --- | --- |
| Office/PDF | Open XML SDK、ClosedXML、QuestPDF、PDFsharp | PdfPig 封装在 adapter 后；PPT 暂缓 |
| 报表 | QuestPDF；Tiptap + 版本化 ProseMirror JSON | 不建设业务人员拖拽式单据设计器 |
| 发票 | XML/XBRL 优先；ZXing.Net | PaddleOCR 私有 worker；OFD 隔离 adapter |
| 标签打印 | PDFsharp、ZPL、ZXing.Net | Etiket 待 Node 24 后用于前端预览；运行时版本满足不等于已启用 |
| 音视频 | FFmpeg、ffprobe | 独立可选 worker，不进入默认 Web 镜像 |

### 工具职责

- QuestPDF：报价单、生产流转单、检验报告、入库单、发货单等代码优先业务单据。
- PDFsharp：标签 PDF、毫米级绝对定位和低层 PDF 绘制。
- Tiptap：合同、手册和报告正文，不承担单据或标签排版。
- Open XML SDK：Word 受控模板与 OOXML 底层处理。
- ClosedXML：常规 Excel 导入导出；超大 XLSX 使用 Open XML 流式写入。
- ZXing.Net：Apache-2.0 开源核心；图像 binding 依赖另行做许可证检查。
- FFmpeg/ffprobe：参数白名单，临时产物验证后原子发布；Web 进程不接受任意 FFmpeg 命令行。

## 已考虑的替代方案

原文明确排除 MiniWord、LibreOffice 常驻转换服务、一个库或一个 AST 包办所有格式、OCR 结果直接成为账务真相，以及前端 SVG/Canvas 结果直接成为生产打印真相。原文未提供逐项落选比较，本次不补造理由。

## 后果与实施边界

工具承担格式处理，业务消费与事实校验由所属子域承担；拆分 adapter 和 worker 保留部署、许可证及资源边界，不能靠一个万能模型统一。文档体系治理只治理文档事实源，不承载运行时文档处理能力。

当前只固化技术裁决，不批量创建实施票。后续每个子域从代表性样本出发，逐张创建 scope:S/M 票；单票只含一个 adapter，或「公共实现 + 一个首篇消费者」。本记录不复制已有产品任务、标签生命周期任务或交付进度，也不引入运行时库。

## 复评边界

原裁决未单列复评触发条件。改变上述正式基线、子域所有权或部署边界时，按 [决策记录 Governance](../governance/decisions/records.md) 新建取代记录；原文中的可选与暂缓项不能据此直接成为正式基线。
