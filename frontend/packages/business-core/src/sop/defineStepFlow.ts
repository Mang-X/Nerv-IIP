export type StepFlowContext = object

export interface StepFlowStep<TCtx> {
  id: string
  /** 该步是否已完成（数据驱动，不在多处散落流程逻辑）。 */
  done: (ctx: TCtx) => boolean
}

export interface StepFlow<TCtx> {
  id: string
  steps: StepFlowStep<TCtx>[]
  currentStep: (ctx: TCtx) => StepFlowStep<TCtx>
  isComplete: (ctx: TCtx) => boolean
  progress: (ctx: TCtx) => { completed: number; total: number }
}

export function defineStepFlow<TCtx extends StepFlowContext>(config: {
  id: string
  steps: StepFlowStep<TCtx>[]
}): StepFlow<TCtx> {
  const { id, steps } = config
  if (steps.length === 0) throw new Error(`stepFlow "${id}" requires at least one step`)
  return {
    id,
    steps,
    currentStep: (ctx) => steps.find((s) => !s.done(ctx)) ?? steps[steps.length - 1],
    isComplete: (ctx) => steps.every((s) => s.done(ctx)),
    progress: (ctx) => ({
      completed: steps.filter((s) => s.done(ctx)).length,
      total: steps.length,
    }),
  }
}
