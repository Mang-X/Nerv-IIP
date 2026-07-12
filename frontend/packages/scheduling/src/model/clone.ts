/** ScheduleModel 是纯 JSON 数据(无 Date / 函数),用 JSON 深拷贝避免 Vue reactive proxy 下
 *  structuredClone 抛 DataCloneError。 */
export function cloneModel<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}
