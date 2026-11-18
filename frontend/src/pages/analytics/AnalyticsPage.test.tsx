import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { AGENT_ID, ADMIN_ID } from '../../test/fixtures';
import { AGENT_PERFORMANCE, OVERVIEW, TRENDS } from '../../test/fixtures.m3';
import { USERS, mockFetch, renderApp, signInAs } from '../../test/utils';

const CSV = 'TicketNumber,Title,Status,Priority,Category,CustomerEmail,AssignedAgentEmail,IsSlaBreached,CreatedAtUtc,ResolvedAtUtc\nND-10001,Cannot access billing,Open,High,General,customer@acme.com,agent@acme.com,False,2026-11-16T18:45:00Z,\n';

function mockAnalytics() {
  return mockFetch({
    'GET /api/v1/analytics/overview': (call) => {
      const days = Number(new URL(call.url, 'http://l').searchParams.get('days'));
      return { body: days === 7 ? { ...OVERVIEW, totalTickets: 61 } : OVERVIEW };
    },
    'GET /api/v1/analytics/tickets/trends': { body: TRENDS },
    'GET /api/v1/analytics/agents/performance': { body: AGENT_PERFORMANCE },
    'GET /api/v1/analytics/export/tickets.csv': {
      body: CSV,
      contentType: 'text/csv; charset=utf-8',
      headers: { 'Content-Disposition': 'attachment; filename="nexusdesk-tickets-report.csv"' },
    },
  });
}

describe('Analytics dashboard', () => {
  it('renders KPI cards and refetches every panel when the lookback window changes', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    const { find } = mockAnalytics();
    renderApp('/analytics');

    await waitFor(() => expect(screen.getByTestId('kpi-totalTickets')).toHaveTextContent('248'));
    expect(screen.getByTestId('kpi-slaComplianceRate')).toHaveTextContent('98.4%');
    expect(screen.getByTestId('kpi-averageFirstResponseMinutes')).toHaveTextContent('14m');
    expect(screen.getByTestId('kpi-averageResolutionMinutes')).toHaveTextContent('1h 37m');
    expect(screen.getByTestId('kpi-csatAverageScore')).toHaveTextContent('4.85 / 5');
    expect(screen.getByTestId('kpi-activeChatSessions')).toHaveTextContent('3');
    expect(screen.getByTestId('kpi-publishedKbArticles')).toHaveTextContent('28');
    expect(find('GET', '/api/v1/analytics/overview')[0].url).toContain('days=30');

    await user.click(screen.getByRole('radio', { name: '7 days' }));
    await waitFor(() => expect(screen.getByTestId('kpi-totalTickets')).toHaveTextContent('61'));
    for (const path of ['/analytics/overview', '/analytics/tickets/trends', '/analytics/agents/performance']) {
      expect(find('GET', path).at(-1)!.url).toContain('days=7');
    }
    expect(find('GET', '/analytics/overview')[0].headers.get('X-Tenant-Subdomain')).toBe('acme');
  });

  it('draws the daily trend chart with legend, crosshair tooltip and a table view', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    mockAnalytics();
    renderApp('/analytics');

    const chart = await screen.findByTestId('trend-chart');
    expect(chart.querySelectorAll('path[data-series]')).toHaveLength(3);
    expect(screen.getByText('103 created · 100 resolved · 3 SLA breaches')).toBeInTheDocument();
    const legend = screen.getByRole('list', { name: 'Legend' });
    expect(within(legend).getByText('Created')).toBeInTheDocument();
    expect(within(legend).getByText('SLA breached')).toBeInTheDocument();

    // Keyboard crosshair: → steps to the first day.
    const hitArea = within(chart).getByLabelText(/Chart hover area/);
    fireEvent.keyDown(hitArea, { key: 'ArrowRight' });
    fireEvent.keyDown(hitArea, { key: 'ArrowRight' });
    const tip = screen.getByTestId('trend-tooltip');
    expect(tip).toHaveTextContent('Nov 11');
    expect(tip).toHaveTextContent('40 Created');
    expect(tip).toHaveTextContent('38 Resolved');
    expect(tip).toHaveTextContent('0 SLA breached');

    await user.click(screen.getByRole('button', { name: 'Table' }));
    const row = screen.getByTestId('trend-row-2026-11-12');
    expect(within(row).getAllByRole('cell').map((c) => c.textContent)).toEqual(['Nov 12', '28', '30', '2']);
  });

  it('ranks the agent performance scoreboard by tickets resolved', async () => {
    signInAs(USERS.acmeAdmin);
    mockAnalytics();
    renderApp('/analytics');

    await screen.findByTestId(`agent-row-${AGENT_ID}`);
    const rows = screen.getAllByTestId(/^agent-row-/);
    expect(rows.map((r) => r.getAttribute('data-testid'))).toEqual([`agent-row-${AGENT_ID}`, `agent-row-${ADMIN_ID}`]);
    expect(rows[0]).toHaveTextContent('Acme Agent');
    expect(rows[0]).toHaveTextContent('99.1%');
    expect(rows[1]).toHaveTextContent('75.0%');
  });

  it('exports the tickets CSV as an authenticated blob download', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAdmin);
    const { find } = mockAnalytics();
    const createObjectURL = vi.fn(() => 'blob:csv');
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() }));
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    renderApp('/analytics');

    await waitFor(() => expect(screen.getByTestId('kpi-totalTickets')).toHaveTextContent('248'));
    await user.click(screen.getByRole('radio', { name: '90 days' }));
    await user.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() => expect(click).toHaveBeenCalled());
    const call = find('GET', '/api/v1/analytics/export/tickets.csv')[0];
    expect(call.url).toContain('days=90');
    expect(call.headers.get('Authorization')).toBe('Bearer token-TenantAdmin');
    expect(call.headers.get('X-Tenant-Subdomain')).toBe('acme');
    expect((click.mock.contexts[0] as HTMLAnchorElement).download).toBe('nexusdesk-tickets-report.csv');
    const blob = (createObjectURL.mock.calls[0] as unknown as [Blob])[0];
    expect(await blob.text()).toContain('TicketNumber,Title,Status');
    expect(await screen.findByText('CSV exported')).toBeInTheDocument();
  });
});
