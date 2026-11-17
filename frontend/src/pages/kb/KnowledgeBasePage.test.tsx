import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { CreateKbArticleRequest } from '../../lib/types';
import { KB_CATEGORIES, KB_SEARCH, kbArticle } from '../../test/fixtures';
import { USERS, mockFetch, renderApp, signInAs } from '../../test/utils';

function mockKb() {
  const article = kbArticle();
  return mockFetch({
    'GET /api/v1/kb/categories': { body: KB_CATEGORIES },
    'GET /api/v1/kb/articles': (call) => {
      const categoryId = new URL(call.url, 'http://l').searchParams.get('categoryId');
      const items = [
        article,
        kbArticle({ id: 'kb-2', slug: 'inviting-agents', title: 'Inviting your first agents', categoryId: 'cat-start', categoryName: 'Getting Started', status: 'Draft' }),
      ].filter((a) => !categoryId || a.categoryId === categoryId);
      return { body: { items, totalCount: items.length, page: 1, pageSize: 50, totalPages: 1 } };
    },
    'GET /api/v1/kb/search': { body: KB_SEARCH },
    [`GET /api/v1/kb/articles/${article.slug}`]: { body: article },
    'POST /api/v1/kb/articles': (call) => {
      const b = call.body as CreateKbArticleRequest;
      return { status: 201, body: kbArticle({ id: 'kb-new', title: b.title, slug: b.slug, status: 'Published' }) };
    },
    'GET /api/v1/kb/articles/configuring-sso-with-okta': { body: kbArticle({ id: 'kb-new', title: 'Configuring SSO with Okta', slug: 'configuring-sso-with-okta' }) },
  });
}

describe('Knowledge base', () => {
  it('runs deflection search and renders relevance badges', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeCustomer);
    const { find } = mockKb();
    renderApp('/portal/kb'); // customers land on the shared Help Center

    expect(await screen.findByRole('heading', { name: 'Help Center' })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Search articles'), 'cannot login to sso');

    const top = await screen.findByTestId('kb-result-troubleshooting-sso-login-failures');
    expect(within(top).getByText('94% match')).toBeInTheDocument();
    expect(within(top).getByText('42 found helpful')).toBeInTheDocument();
    expect(within(screen.getByTestId('kb-result-inviting-agents')).getByText('31% match')).toBeInTheDocument();

    const search = find('GET', '/api/v1/kb/search').at(-1)!;
    expect(new URL(search.url, 'http://l').searchParams.get('q')).toBe('cannot login to sso');
    expect(search.url).toContain('limit=10');
    expect(search.headers.get('X-Tenant-Subdomain')).toBe('acme');

    // Category chips narrow the results.
    await user.click(within(screen.getByRole('group', { name: 'Filter by category' })).getByRole('button', { name: /Security/ }));
    await waitFor(() => expect(screen.queryByTestId('kb-result-inviting-agents')).not.toBeInTheDocument());
    expect(screen.getByTestId('kb-result-troubleshooting-sso-login-failures')).toBeInTheDocument();
  });

  it('opens the full article reader by slug and renders its markdown', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    mockKb();
    renderApp('/kb');

    await user.click(await screen.findByTestId('kb-article-troubleshooting-sso-login-failures'));
    const reader = await screen.findByTestId('kb-reader');
    expect(within(reader).getByRole('heading', { name: 'Fixing SSO' })).toBeInTheDocument();
    expect(within(reader).getByText('clock skew').tagName).toBe('STRONG');
    expect(within(reader).getByText('Sync NTP').tagName).toBe('LI');
    expect(screen.getByTestId('location')).toHaveTextContent('/kb');
  });

  it('filters the browse list by category and shows drafts to staff', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    const { find } = mockKb();
    renderApp('/kb');

    expect(await screen.findByTestId('kb-article-inviting-agents')).toHaveTextContent('Draft');
    await user.click(within(screen.getByRole('group', { name: 'Filter by category' })).getByRole('button', { name: /Security/ }));
    await waitFor(() => expect(screen.queryByTestId('kb-article-inviting-agents')).not.toBeInTheDocument());
    expect(find('GET', '/api/v1/kb/articles').at(-1)!.url).toContain('categoryId=cat-sec');
  });

  it('lets staff author a new article', async () => {
    const user = userEvent.setup();
    signInAs(USERS.acmeAgent);
    const { find } = mockKb();
    renderApp('/kb');

    await user.click(await screen.findByRole('button', { name: 'New article' }));
    const dialog = await screen.findByRole('dialog', { name: 'New article' });
    await waitFor(() => expect(within(dialog).getByLabelText('Category')).toHaveValue('cat-start'));
    await user.selectOptions(within(dialog).getByLabelText('Category'), 'cat-sec');
    await user.type(within(dialog).getByLabelText('Title'), 'Configuring SSO with Okta');
    expect(within(dialog).getByLabelText('Slug')).toHaveValue('configuring-sso-with-okta');
    await user.type(within(dialog).getByLabelText('Summary'), 'SAML + OIDC.');
    await user.type(within(dialog).getByLabelText('Body (Markdown)'), '# Okta Setup');
    await user.type(within(dialog).getByLabelText('Tags'), 'sso, Okta');
    await user.click(within(dialog).getByRole('button', { name: 'Publish article' }));

    await waitFor(() => expect(find('POST', '/api/v1/kb/articles')).toHaveLength(1));
    expect(find('POST', '/api/v1/kb/articles')[0].body).toEqual({
      categoryId: 'cat-sec',
      title: 'Configuring SSO with Okta',
      slug: 'configuring-sso-with-okta',
      summary: 'SAML + OIDC.',
      bodyMarkdown: '# Okta Setup',
      tags: ['sso', 'okta'],
      publishImmediately: true,
    });
    expect(await screen.findByTestId('kb-reader')).toBeInTheDocument();
  });

  it('hides authoring from customers', async () => {
    signInAs(USERS.acmeCustomer);
    mockKb();
    renderApp('/kb');
    await screen.findByTestId('kb-article-troubleshooting-sso-login-failures');
    expect(screen.queryByRole('button', { name: 'New article' })).not.toBeInTheDocument();
  });
});
