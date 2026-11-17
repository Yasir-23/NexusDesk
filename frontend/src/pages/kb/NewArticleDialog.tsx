import { useEffect, useState, type FormEvent } from 'react';
import { Button, Dialog, ErrorPanel, Field, Input, Select, Textarea } from '../../components/ui';
import { slugify, useCreateKbArticle } from '../../lib/kb';
import type { KbArticle, KbCategory } from '../../lib/types';
import { toast } from '../../stores/toast';

/** `POST /api/v1/kb/articles` (staff). */
export function NewArticleDialog({
  open,
  onClose,
  categories,
  onCreated,
}: {
  open: boolean;
  onClose: () => void;
  categories: KbCategory[];
  onCreated: (article: KbArticle) => void;
}) {
  const create = useCreateKbArticle();
  const [categoryId, setCategoryId] = useState('');
  const [title, setTitle] = useState('');
  const [slug, setSlug] = useState('');
  const [slugTouched, setSlugTouched] = useState(false);
  const [summary, setSummary] = useState('');
  const [body, setBody] = useState('');
  const [tags, setTags] = useState('');
  const [publish, setPublish] = useState(true);

  useEffect(() => {
    if (open && !categoryId && categories.length > 0) setCategoryId(categories[0].id);
  }, [open, categoryId, categories]);

  const reset = () => {
    setTitle('');
    setSlug('');
    setSlugTouched(false);
    setSummary('');
    setBody('');
    setTags('');
    setPublish(true);
    create.reset();
  };

  const close = () => {
    reset();
    onClose();
  };

  const effectiveSlug = slugTouched ? slug : slugify(title);
  const canSubmit = Boolean(categoryId && title.trim() && effectiveSlug && body.trim());

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    try {
      const article = await create.mutateAsync({
        categoryId,
        title: title.trim(),
        slug: effectiveSlug,
        summary: summary.trim(),
        bodyMarkdown: body,
        tags: tags
          .split(',')
          .map((t) => t.trim().toLowerCase())
          .filter(Boolean),
        publishImmediately: publish,
      });
      toast.success(publish ? 'Article published' : 'Draft saved', article.title);
      reset();
      onCreated(article);
    } catch {
      /* rendered inline */
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      size="lg"
      title="New article"
      description="Published articles feed deflection search and AI-grounded reply suggestions."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form="new-article-form" loading={create.isPending} disabled={!canSubmit}>
            {publish ? 'Publish article' : 'Save draft'}
          </Button>
        </>
      }
    >
      <form id="new-article-form" onSubmit={submit} className="space-y-4">
        {create.error && <ErrorPanel error={create.error} />}
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Category">
            {(id) => (
              <Select id={id} value={categoryId} onChange={(e) => setCategoryId(e.target.value)} required>
                {categories.length === 0 && <option value="">No categories</option>}
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            )}
          </Field>
          <Field label="Tags" hint="Comma-separated">
            {(id) => <Input id={id} value={tags} onChange={(e) => setTags(e.target.value)} placeholder="sso, okta, security" />}
          </Field>
        </div>
        <Field label="Title">
          {(id) => <Input id={id} value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Configuring Single Sign-On with Okta" required />}
        </Field>
        <Field label="Slug" hint="Used in article URLs; generated from the title until edited">
          {(id) => (
            <Input
              id={id}
              value={effectiveSlug}
              onChange={(e) => {
                setSlugTouched(true);
                setSlug(slugify(e.target.value) || e.target.value.toLowerCase());
              }}
              className="font-mono text-xs"
            />
          )}
        </Field>
        <Field label="Summary">
          {(id) => <Input id={id} value={summary} onChange={(e) => setSummary(e.target.value)} placeholder="Step-by-step setup guide for SAML 2.0 and OIDC in Okta." />}
        </Field>
        <Field label="Body (Markdown)">
          {(id) => (
            <Textarea
              id={id}
              rows={8}
              value={body}
              onChange={(e) => setBody(e.target.value)}
              className="font-mono text-xs"
              placeholder={'# Okta Setup\n\nNavigate to Applications…'}
              required
            />
          )}
        </Field>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={publish}
            onChange={(e) => setPublish(e.target.checked)}
            className="h-4 w-4 rounded border-zinc-300 text-indigo-600 focus:ring-indigo-500"
          />
          Publish immediately
        </label>
      </form>
    </Dialog>
  );
}
