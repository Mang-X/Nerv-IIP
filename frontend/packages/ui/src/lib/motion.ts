export const nervMotion = {
  fastInvoke: {
    duration: 0.187,
    ease: [0, 0, 0, 1] as const,
  },
  fastInvokeMedium: {
    duration: 0.333,
    ease: [0, 0, 0, 1] as const,
  },
  fastInvokeLong: {
    duration: 0.5,
    ease: [0, 0, 0, 1] as const,
  },
  strongInvoke: {
    duration: 0.667,
    ease: [0.13, 1.62, 0, 0.92] as const,
  },
  fastDismiss: {
    duration: 0.187,
    ease: [0, 0, 0, 1] as const,
  },
  fastDismissMedium: {
    duration: 0.333,
    ease: [0, 0, 0, 1] as const,
  },
  fastDismissLong: {
    duration: 0.5,
    ease: [0, 0, 0, 1] as const,
  },
  softDismiss: {
    duration: 0.167,
    ease: [1, 0, 1, 1] as const,
  },
  pointToPointShort: {
    duration: 0.187,
    ease: [0.55, 0.55, 0, 1] as const,
  },
  pointToPoint: {
    duration: 0.333,
    ease: [0.55, 0.55, 0, 1] as const,
  },
  pointToPointLong: {
    duration: 0.5,
    ease: [0.55, 0.55, 0, 1] as const,
  },
  fade: {
    duration: 0.083,
    ease: 'linear' as const,
  },
} as const
