/**
 * 产品工程域 · 前端受控值常量。
 *
 * 这些取值后端存自由字符串、且**没有对应 CodeSet**（见
 * `docs/architecture/master-data-dictionary-rules.md` §2 权威清单），所以由前端集中固定，
 * 避免各页手输拼写漂移。后端补字典后改由 `?codeSet=` 实时拉取，本文件降级为兜底。
 */
import type { RefOption } from './masterDataReference'

/** 工程文档类型（EngineeringDocument.DocumentType）。 */
export const ENGINEERING_DOCUMENT_TYPE_OPTIONS: RefOption[] = [
  { value: 'drawing', label: '图纸' },
  { value: 'cad', label: 'CAD 模型' },
  { value: 'specification', label: '规格书' },
  { value: 'process-sheet', label: '工艺卡' },
  { value: 'work-instruction', label: '作业指导书' },
  { value: 'inspection-standard', label: '检验标准' },
  { value: 'test-report', label: '试验报告' },
  { value: 'material-certificate', label: '材质证明' },
]

/**
 * 存量文档里的既有类型码 → 中文（**只用于显示，不进新建/筛选下拉**）。
 *
 * 后端 `EngineeringDocument.DocumentType` 是自由字符串，没有白名单：现场既有数据与
 * 世界史种子写的是 `sop` / `inspection-spec` / `process-card`
 * （`WorldHistoryEngineeringSpec.DocumentType*`），与上面这份前端受控值并不同源。
 * 不收这三条，「类型」列就会直接印出英文码；收进下拉又会和「作业指导书 / 检验标准 /
 * 工艺卡」语义重叠、让新建时二选一犯难，所以单独放一张只读别名表。
 */
export const ENGINEERING_DOCUMENT_TYPE_ALIASES: Readonly<Record<string, string>> = {
  sop: '标准作业指导书',
  'inspection-spec': '检验规范',
  'process-card': '过程卡',
}
