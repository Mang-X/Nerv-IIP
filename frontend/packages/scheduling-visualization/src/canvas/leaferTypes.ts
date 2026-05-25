export interface LeaferRectInput {
  id: string
  x: number
  y: number
  width: number
  height: number
  fill?: string
  stroke?: string
  cornerRadius?: number
  metadata?: Record<string, string | number | boolean>
}

export interface LeaferTextInput {
  id: string
  x: number
  y: number
  text: string
  fill?: string
  fontSize?: number
  metadata?: Record<string, string | number | boolean>
}

export interface LeaferPathInput {
  id: string
  points: Array<{ x: number; y: number }>
  stroke?: string
  fill?: string
  metadata?: Record<string, string | number | boolean>
}

export interface LeaferSurface {
  clear(): void
  addRect(input: LeaferRectInput): void
  addText(input: LeaferTextInput): void
  addPath(input: LeaferPathInput): void
  flush(): void
  dispose(): void
}
