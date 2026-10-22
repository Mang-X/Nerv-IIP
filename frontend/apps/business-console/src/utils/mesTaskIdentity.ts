import { readFaceText } from './readFace'

/** 展示值不承担路由身份；系统 UUID 只供完整身份查看，不伪装成业务编号。 */
export function operationTaskLabel(task: {
  operationTaskNo?: string | null
  operationTaskId?: string | null
  operationSequence?: number | null
}) {
  return (
    task.operationTaskNo?.trim() ||
    readFaceText(
      task.operationTaskId,
      task.operationSequence == null ? '未提供人读编号' : `工序 ${task.operationSequence}`,
    )
  )
}
