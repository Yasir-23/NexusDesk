import type { IsolationStrategy, Role, TenantPlanTier } from './types';

export const DEMO_PASSWORD = 'Password123!';

export interface DemoAccount {
  email: string;
  role: Role;
  /** `null` = platform scope (no X-Tenant-Subdomain). */
  subdomain: string | null;
  label: string;
  planTier: TenantPlanTier;
  isolation: IsolationStrategy | 'Platform';
}

/** The six seeded accounts from contract §2. */
export const DEMO_ACCOUNTS: DemoAccount[] = [
  { email: 'superadmin@nexusdesk.com', role: 'SuperAdmin', subdomain: null, label: 'Platform', planTier: 'Enterprise', isolation: 'Platform' },
  { email: 'admin@acme.com', role: 'TenantAdmin', subdomain: 'acme', label: 'Acme', planTier: 'Pro', isolation: 'SharedDatabase' },
  { email: 'agent@acme.com', role: 'Agent', subdomain: 'acme', label: 'Acme', planTier: 'Pro', isolation: 'SharedDatabase' },
  { email: 'customer@acme.com', role: 'Customer', subdomain: 'acme', label: 'Acme', planTier: 'Pro', isolation: 'SharedDatabase' },
  { email: 'admin@beta.com', role: 'TenantAdmin', subdomain: 'beta', label: 'Beta', planTier: 'Free', isolation: 'SharedDatabase' },
  { email: 'admin@globex.com', role: 'TenantAdmin', subdomain: 'globex', label: 'Globex', planTier: 'Enterprise', isolation: 'DedicatedDatabase' },
];

export const KNOWN_TENANTS = [
  { subdomain: 'acme', name: 'Acme Corporation', planTier: 'Pro' },
  { subdomain: 'beta', name: 'Beta Innovations', planTier: 'Free' },
  { subdomain: 'globex', name: 'Globex Global', planTier: 'Enterprise' },
] as const;
