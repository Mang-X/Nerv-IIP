export const messages = {
  'zh-CN': {
    app: {
      brand: 'Nerv-IIP',
      tagline: '工业物联网控制平面',
      description: '通过网关管理应用实例和操作任务。',
      title: 'Nerv-IIP 控制台',
    },
    action: {
      retry: '重试',
    },
    breadcrumb: {
      dashboard: '工作台',
    },
    login: {
      title: '登录',
      description: '使用控制台账号继续。',
      loginName: '登录名',
      loginNameHint: '本地种子管理员使用 admin。',
      password: '密码',
      pending: '正在登录',
    },
    nav: {
      platform: '平台',
      instances: '实例',
      notifications: '通知',
      business: '业务平台',
      iam: '身份与访问',
      users: '用户',
      roles: '角色',
      sessions: '会话',
      notificationInbox: '通知中心',
      notificationDlq: '死信队列',
      authenticatedUser: '已登录用户',
      signOut: '退出登录',
    },
    routes: {
      instances: '实例',
      business: '业务平台状态',
      login: '登录',
    },
    home: {
      latestOperationTask: '最新操作任务',
      unableToLoadInstanceDetail: '无法加载实例详情',
    },
  },
  'en-US': {
    app: {
      brand: 'Nerv-IIP',
      tagline: 'Industrial IoT Control Plane',
      description: 'Manage application instances and operation tasks through the Gateway.',
      title: 'Nerv-IIP Console',
    },
    action: {
      retry: 'Retry',
    },
    breadcrumb: {
      dashboard: 'Dashboard',
    },
    login: {
      title: 'Sign in',
      description: 'Use your Console account to continue.',
      loginName: 'Login name',
      loginNameHint: 'Seeded local admin uses admin.',
      password: 'Password',
      pending: 'Signing in',
    },
    nav: {
      platform: 'Platform',
      instances: 'Instances',
      notifications: 'Notifications',
      business: 'Business',
      iam: 'IAM',
      users: 'Users',
      roles: 'Roles',
      sessions: 'Sessions',
      notificationInbox: 'Inbox',
      notificationDlq: 'Dead letters',
      authenticatedUser: 'Authenticated user',
      signOut: 'Sign out',
    },
    routes: {
      instances: 'Instances',
      business: 'Business status',
      login: 'Sign in',
    },
    home: {
      latestOperationTask: 'Latest operation task',
      unableToLoadInstanceDetail: 'Unable to load instance detail',
    },
  },
} as const

export type MessageSchema = typeof messages['zh-CN']
