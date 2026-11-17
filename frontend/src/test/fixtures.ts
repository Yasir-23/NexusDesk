import type {
  ChatMessage,
  ChatSession,
  KbArticle,
  KbCategory,
  KbSearchResult,
  PagedTicketsResponse,
  TenantUser,
  TicketDetails,
  TicketSummary,
} from '../lib/types';

export const TENANT_ID = 'b7e43bc4-a5c7-4c8f-a470-7277ce9823f4';
export const ADMIN_ID = '22222222-2222-2222-2222-222222222221';
export const AGENT_ID = '22222222-2222-2222-2222-222222222222';
export const CUSTOMER_ID = '22222222-2222-2222-2222-222222222223';

function user(id: string, email: string, fullName: string, role: TenantUser['role']): TenantUser {
  return {
    id,
    tenantId: TENANT_ID,
    email,
    fullName,
    phoneNumber: null,
    role,
    isActive: true,
    isAnonymized: false,
    lastLoginAtUtc: null,
    createdAtUtc: '2026-11-10T00:00:00Z',
  };
}

export const TENANT_USERS: TenantUser[] = [
  user(ADMIN_ID, 'admin@acme.com', 'Acme Admin', 'TenantAdmin'),
  user(AGENT_ID, 'agent@acme.com', 'Acme Agent', 'Agent'),
  user(CUSTOMER_ID, 'customer@acme.com', 'Acme Customer', 'Customer'),
];

const future = (h: number) => new Date(Date.now() + h * 3_600_000).toISOString();

export function ticketSummary(overrides: Partial<TicketSummary> = {}): TicketSummary {
  return {
    id: '652f5d1c-aed3-426d-aad5-48273c5140ad',
    tenantId: TENANT_ID,
    ticketNumber: 'ND-10001',
    title: 'Cannot access billing portal from Chrome',
    status: 'Open',
    priority: 'High',
    channel: 'WebPortal',
    customerId: CUSTOMER_ID,
    assignedAgentId: AGENT_ID,
    firstResponseDueUtc: future(1.5),
    resolutionDueUtc: future(8),
    firstRespondedAtUtc: null,
    resolvedAtUtc: null,
    closedAtUtc: null,
    createdAtUtc: '2026-11-16T18:45:00Z',
    updatedAtUtc: '2026-11-16T18:50:00Z',
    rowVersion: 2,
    tags: ['billing', 'auth'],
    isSlaBreached: false,
    slaBreachedAtUtc: null,
    ...overrides,
  };
}

export const QUEUE: TicketSummary[] = [
  ticketSummary(),
  ticketSummary({ id: 't-2', ticketNumber: 'ND-10002', title: 'Password reset email never arrives', status: 'New', priority: 'Medium', assignedAgentId: null }),
  ticketSummary({
    id: 't-3',
    ticketNumber: 'ND-10003',
    title: 'API returns 500 on export',
    status: 'Open',
    priority: 'Urgent',
    assignedAgentId: ADMIN_ID,
    isSlaBreached: true,
    slaBreachedAtUtc: '2026-11-16T19:45:00Z',
  }),
];

export function paged(items: TicketSummary[]): PagedTicketsResponse {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

export function ticketDetails(overrides: Partial<TicketDetails> = {}): TicketDetails {
  return {
    ...ticketSummary(),
    description: 'Receiving a blank page and 403 Forbidden after MFA prompt.',
    resolutionNotes: null,
    comments: [
      {
        id: 'c-1',
        authorUserId: CUSTOMER_ID,
        isCustomer: true,
        type: 'PublicReply',
        body: 'It started after the MFA rollout.',
        createdAtUtc: '2026-11-16T18:46:00Z',
      },
      {
        id: 'c-2',
        authorUserId: ADMIN_ID,
        isCustomer: false,
        type: 'InternalNote',
        body: 'Probably the stale session cookie bug — check auth server cache.',
        createdAtUtc: '2026-11-16T18:48:00Z',
      },
    ],
    activityLogs: [
      {
        id: 'a-1',
        performedByUserId: CUSTOMER_ID,
        action: 'Created',
        oldValue: null,
        newValue: 'New',
        timestampUtc: '2026-11-16T18:45:00Z',
      },
    ],
    ...overrides,
  };
}

export const CHAT_SESSIONS: ChatSession[] = [
  {
    id: 'chat-waiting',
    tenantId: TENANT_ID,
    customerId: CUSTOMER_ID,
    assignedAgentId: null,
    subject: 'Pre-sales inquiry regarding Enterprise dedicated database',
    status: 'Waiting',
    convertedTicketId: null,
    createdAtUtc: '2026-11-16T19:10:00Z',
    closedAtUtc: null,
    messages: [],
  },
  {
    id: 'chat-active',
    tenantId: TENANT_ID,
    customerId: CUSTOMER_ID,
    assignedAgentId: AGENT_ID,
    subject: 'Invoice question',
    status: 'Active',
    convertedTicketId: null,
    createdAtUtc: '2026-11-16T18:10:00Z',
    closedAtUtc: null,
    messages: [],
  },
];

export function chatMessage(overrides: Partial<ChatMessage> = {}): ChatMessage {
  return {
    id: 'm-1',
    tenantId: TENANT_ID,
    chatSessionId: 'chat-waiting',
    senderUserId: CUSTOMER_ID,
    senderRole: 'Customer',
    content: 'Hi, does Enterprise include a dedicated database?',
    sentAtUtc: '2026-11-16T19:11:00Z',
    ...overrides,
  };
}

export const KB_CATEGORIES: KbCategory[] = [
  {
    id: 'cat-start',
    tenantId: TENANT_ID,
    name: 'Getting Started',
    slug: 'getting-started',
    description: null,
    displayOrder: 1,
    createdAtUtc: '2026-11-10T00:00:00Z',
    publishedArticlesCount: 4,
  },
  {
    id: 'cat-sec',
    tenantId: TENANT_ID,
    name: 'Security',
    slug: 'security',
    description: null,
    displayOrder: 2,
    createdAtUtc: '2026-11-10T00:00:00Z',
    publishedArticlesCount: 2,
  },
];

export function kbArticle(overrides: Partial<KbArticle> = {}): KbArticle {
  return {
    id: '60606060-7070-8080-9090-a0a0a0a0a0a0',
    tenantId: TENANT_ID,
    categoryId: 'cat-sec',
    categoryName: 'Security',
    title: 'Troubleshooting SSO Login Failures',
    slug: 'troubleshooting-sso-login-failures',
    summary: 'Fix common SAML token and clock-skew errors.',
    bodyMarkdown: '# Fixing SSO\n\nCheck **clock skew** first.\n\n- Sync NTP\n- Re-upload metadata',
    tags: ['sso'],
    status: 'Published',
    authorUserId: AGENT_ID,
    viewCount: 12,
    helpfulCount: 42,
    notHelpfulCount: 1,
    publishedAtUtc: '2026-11-12T00:00:00Z',
    createdAtUtc: '2026-11-12T00:00:00Z',
    updatedAtUtc: '2026-11-12T00:00:00Z',
    ...overrides,
  };
}

export const KB_SEARCH: KbSearchResult[] = [
  {
    id: '60606060-7070-8080-9090-a0a0a0a0a0a0',
    title: 'Troubleshooting SSO Login Failures',
    slug: 'troubleshooting-sso-login-failures',
    summary: 'Fix common SAML token and clock-skew errors.',
    categoryName: 'Security',
    relevanceScore: 0.94,
    helpfulCount: 42,
  },
  {
    id: 'kb-2',
    title: 'Inviting your first agents',
    slug: 'inviting-agents',
    summary: 'Add teammates to your workspace.',
    categoryName: 'Getting Started',
    relevanceScore: 0.31,
    helpfulCount: 3,
  },
];
