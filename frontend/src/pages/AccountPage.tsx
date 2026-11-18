import { KeyRound, ShieldCheck, UserCircle } from 'lucide-react';
import { PageHeader } from '../components/layout/AppShell';
import { RoleBadge } from '../components/domain/Badges';
import { Badge, Card, CardBody, CardHeader, ErrorPanel, Skeleton } from '../components/ui';
import { formatDateTime } from '../lib/format';
import { useMe } from '../lib/queries';
import { useAuthStore } from '../stores/auth';
import { PrivacyCard } from './account/PrivacyCard';

/** `GET /api/v1/auth/me` — profile and effective permissions. */
export function AccountPage() {
  const { data, isLoading, error, refetch } = useMe();
  const { tenantSubdomain, expiresAt } = useAuthStore();

  return (
    <>
      <PageHeader title="My Profile" description="Your identity, role and effective permissions as resolved by the API." />
      {error ? (
        <ErrorPanel error={error} onRetry={() => void refetch()} />
      ) : isLoading || !data ? (
        <Skeleton className="h-64 w-full" />
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          <Card>
            <CardHeader title="Identity" icon={<UserCircle className="h-4 w-4" />} actions={<RoleBadge role={data.role} />} />
            <CardBody>
              <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-3 text-sm">
                <dt className="text-zinc-500">Name</dt>
                <dd className="font-medium">{data.fullName}</dd>
                <dt className="text-zinc-500">Email</dt>
                <dd>{data.email}</dd>
                <dt className="text-zinc-500">Phone</dt>
                <dd>{data.phoneNumber ?? '—'}</dd>
                <dt className="text-zinc-500">Status</dt>
                <dd>
                  <Badge tone={data.isActive ? 'green' : 'red'} dot>
                    {data.isActive ? 'Active' : 'Inactive'}
                  </Badge>
                </dd>
                <dt className="text-zinc-500">SSO</dt>
                <dd>{data.ssoProvider ?? 'Password'}</dd>
                <dt className="text-zinc-500">Last login</dt>
                <dd>{formatDateTime(data.lastLoginAtUtc)}</dd>
                <dt className="text-zinc-500">User ID</dt>
                <dd className="break-all font-mono text-xs">{data.id}</dd>
                <dt className="text-zinc-500">Tenant</dt>
                <dd className="font-mono text-xs">{tenantSubdomain ?? 'platform'}</dd>
              </dl>
            </CardBody>
          </Card>
          <Card>
            <CardHeader
              title="Permissions"
              description={`${data.permissions.length} claims granted to the ${data.role} role`}
              icon={<ShieldCheck className="h-4 w-4" />}
            />
            <CardBody>
              {data.permissions.length ? (
                <ul className="flex flex-wrap gap-1.5">
                  {data.permissions.map((p) => (
                    <li key={p}>
                      <Badge tone="indigo" className="font-mono">
                        {p}
                      </Badge>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="text-sm text-zinc-500">No explicit permission claims.</p>
              )}
              <p className="mt-5 flex items-center gap-1.5 text-xs text-zinc-400">
                <KeyRound className="h-3.5 w-3.5" /> Access token expires {expiresAt ? formatDateTime(new Date(expiresAt).toISOString()) : '—'}
              </p>
            </CardBody>
          </Card>
          {data.role !== 'SuperAdmin' && <PrivacyCard email={data.email} />}
        </div>
      )}
    </>
  );
}
