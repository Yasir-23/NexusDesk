import { Fragment, type ReactNode } from 'react';

/**
 * Minimal, XSS-safe Markdown renderer for KB articles. Builds React elements (never HTML
 * strings) and supports headings, paragraphs, lists, block quotes, fenced code, inline
 * code, bold, italic and http(s)/relative links.
 */
export function Markdown({ source }: { source: string }) {
  return <div className="space-y-3 text-sm leading-relaxed text-zinc-700 dark:text-zinc-300">{renderBlocks(source)}</div>;
}

function renderBlocks(source: string): ReactNode[] {
  const lines = source.replace(/\r\n?/g, '\n').split('\n');
  const out: ReactNode[] = [];
  let i = 0;
  let key = 0;

  while (i < lines.length) {
    const line = lines[i];

    if (!line.trim()) {
      i++;
      continue;
    }

    if (line.startsWith('```')) {
      const code: string[] = [];
      i++;
      while (i < lines.length && !lines[i].startsWith('```')) code.push(lines[i++]);
      i++; // closing fence
      out.push(
        <pre key={key++} className="overflow-x-auto rounded-lg bg-zinc-900 p-3 font-mono text-xs text-zinc-100">
          <code>{code.join('\n')}</code>
        </pre>,
      );
      continue;
    }

    const heading = /^(#{1,4})\s+(.*)$/.exec(line);
    if (heading) {
      const level = heading[1].length;
      const cls = ['', 'text-lg font-semibold', 'text-base font-semibold', 'text-sm font-semibold', 'text-sm font-medium'][level];
      const content = renderInline(heading[2]);
      const h =
        level === 1 ? <h2 className={cls}>{content}</h2> : level === 2 ? <h3 className={cls}>{content}</h3> : <h4 className={cls}>{content}</h4>;
      out.push(
        <div key={key++} className="pt-1 text-zinc-900 dark:text-zinc-100">
          {h}
        </div>,
      );
      i++;
      continue;
    }

    if (/^\s*[-*]\s+/.test(line) || /^\s*\d+[.)]\s+/.test(line)) {
      const ordered = /^\s*\d+[.)]\s+/.test(line);
      const items: string[] = [];
      const re = ordered ? /^\s*\d+[.)]\s+(.*)$/ : /^\s*[-*]\s+(.*)$/;
      while (i < lines.length && re.test(lines[i])) items.push(re.exec(lines[i++])![1]);
      const children = items.map((item, n) => <li key={n}>{renderInline(item)}</li>);
      out.push(
        ordered ? (
          <ol key={key++} className="list-decimal space-y-1 pl-5">
            {children}
          </ol>
        ) : (
          <ul key={key++} className="list-disc space-y-1 pl-5">
            {children}
          </ul>
        ),
      );
      continue;
    }

    if (line.startsWith('>')) {
      const quote: string[] = [];
      while (i < lines.length && lines[i].startsWith('>')) quote.push(lines[i++].replace(/^>\s?/, ''));
      out.push(
        <blockquote key={key++} className="border-l-2 border-indigo-300 pl-3 italic text-zinc-600 dark:text-zinc-400">
          {renderInline(quote.join(' '))}
        </blockquote>,
      );
      continue;
    }

    const para: string[] = [];
    while (
      i < lines.length &&
      lines[i].trim() &&
      !/^(#{1,4}\s|```|>|\s*[-*]\s+|\s*\d+[.)]\s+)/.test(lines[i])
    ) {
      para.push(lines[i++]);
    }
    out.push(<p key={key++}>{renderInline(para.join(' '))}</p>);
  }
  return out;
}

const INLINE = /(`[^`]+`)|(\*\*[^*]+\*\*)|(\*[^*]+\*|_[^_]+_)|(\[[^\]]+\]\([^)\s]+\))/g;

function safeHref(href: string): string | null {
  return /^(https?:\/\/|\/|#)/i.test(href) ? href : null;
}

export function renderInline(text: string): ReactNode[] {
  const out: ReactNode[] = [];
  let last = 0;
  let key = 0;
  for (const m of text.matchAll(INLINE)) {
    if (m.index > last) out.push(<Fragment key={key++}>{text.slice(last, m.index)}</Fragment>);
    const [token] = m;
    if (m[1]) {
      out.push(
        <code key={key++} className="rounded bg-zinc-100 px-1 py-0.5 font-mono text-[0.85em] dark:bg-zinc-800">
          {token.slice(1, -1)}
        </code>,
      );
    } else if (m[2]) {
      out.push(<strong key={key++}>{token.slice(2, -2)}</strong>);
    } else if (m[3]) {
      out.push(<em key={key++}>{token.slice(1, -1)}</em>);
    } else {
      const link = /^\[([^\]]+)\]\(([^)\s]+)\)$/.exec(token)!;
      const href = safeHref(link[2]);
      out.push(
        href ? (
          <a key={key++} href={href} target="_blank" rel="noreferrer noopener" className="text-indigo-600 underline dark:text-indigo-400">
            {link[1]}
          </a>
        ) : (
          <Fragment key={key++}>{link[1]}</Fragment>
        ),
      );
    }
    last = m.index + token.length;
  }
  if (last < text.length) out.push(<Fragment key={key++}>{text.slice(last)}</Fragment>);
  return out;
}
