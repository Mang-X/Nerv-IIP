/**
 * 物料编码 → 基本计量单位的索引：SKU 主数据读面的纯投影。
 *
 * 单位是**物料主档的事实**，不是界面常量：钢材按 kg、油品按 l、计件件号才是 pcs。
 * 写死一个通用单位会让后端单位换算找不到换算关系而整单失败，所以凡是「选完物料自动带出单位」
 * 的地方（ERP 单据行 / 备件换件行 / 库存查询范围 / 物料名解析）都必须按同一口径建索引。
 *
 * 之所以单独成文件而不是塞进 `useBusinessMasterData`：它是**纯函数**，不发查询、不碰 pinia，
 * 各目录 composable 的单测把 `useBusinessMasterData` 整体 mock 掉时，本口径仍走真实实现。
 */
import { computed, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue'

/** 只依赖建索引真正用到的两个字段，读面行类型变宽不影响本函数。 */
export interface SkuBaseUomSource {
  code?: string | null
  baseUomCode?: string | null
}

/**
 * 建「物料编码 → 基本单位」索引。
 *
 * 口径：编码与单位都 trim；**任一为空的行整行跳过**——目录还没到或该物料主档没填单位时，
 * 调用方拿到 `undefined` 后应当等目录/提示去维护主数据，绝不猜一个通用单位。
 */
export function toBaseUomBySku(
  skus: MaybeRefOrGetter<readonly SkuBaseUomSource[]>,
): ComputedRef<Map<string, string>> {
  return computed(() => {
    const map = new Map<string, string>()
    for (const sku of toValue(skus)) {
      const code = sku.code?.trim()
      const uom = sku.baseUomCode?.trim()
      if (code && uom) map.set(code, uom)
    }
    return map
  })
}
