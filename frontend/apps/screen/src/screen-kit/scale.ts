export type ScaleMode = 'fit' | 'width' | 'stretch'

export interface Scale {
  x: number
  y: number
}

/**
 * 计算大屏舞台相对设计基准（默认 1920×1080）的缩放比。
 * - fit：等比缩放取小边，letterbox 留边，整屏完整可见（大屏默认）
 * - width：按宽度等比，高度可能溢出（适合超宽拼接屏）
 * - stretch：宽高各自拉伸，非等比（会变形，一般不用）
 */
export function computeScale(
  viewportW: number,
  viewportH: number,
  designW: number,
  designH: number,
  mode: ScaleMode = 'fit',
): Scale {
  if (designW <= 0 || designH <= 0 || viewportW <= 0 || viewportH <= 0) {
    return { x: 1, y: 1 }
  }
  switch (mode) {
    case 'width': {
      const s = viewportW / designW
      return { x: s, y: s }
    }
    case 'stretch':
      return { x: viewportW / designW, y: viewportH / designH }
    case 'fit':
    default: {
      const s = Math.min(viewportW / designW, viewportH / designH)
      return { x: s, y: s }
    }
  }
}
