import { useMemo, type MouseEvent } from 'react';
import { renderMarkdown } from '../lib/markdown';

interface Props {
  content: string;
  noteId: string;
  linkTitles?: ReadonlySet<string>;
  onWikilink?: (title: string, unresolved: boolean) => void;
}

export function MarkdownPreview({ content, noteId, linkTitles, onWikilink }: Props) {
  const html = useMemo(
    () => renderMarkdown(content, noteId, linkTitles),
    [content, noteId, linkTitles],
  );

  function onClick(event: MouseEvent<HTMLElement>) {
    const anchor = (event.target as HTMLElement).closest<HTMLAnchorElement>('a[data-wikilink]');
    if (!anchor) {
      return;
    }

    event.preventDefault();
    const title = anchor.dataset.wikilink;
    if (title) {
      onWikilink?.(title, anchor.classList.contains('wikilink-unresolved'));
    }
  }

  if (!content.trim()) {
    return (
      <div className="grid h-full place-items-center px-8 text-center text-sm text-[var(--text-faint)]">
        开始输入，这里会实时显示预览。
      </div>
    );
  }

  return (
    <div className="h-full overflow-y-auto">
      <article
        className="md-preview mx-auto max-w-3xl px-5 py-8 sm:px-8 sm:py-10"
        onClick={onClick}
        dangerouslySetInnerHTML={{ __html: html }}
      />
    </div>
  );
}
