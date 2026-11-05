import { useMemo } from 'react';
import { renderMarkdown } from '../lib/markdown';

interface Props {
  content: string;
  noteId: string;
}

export function MarkdownPreview({ content, noteId }: Props) {
  const html = useMemo(() => renderMarkdown(content, noteId), [content, noteId]);

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
        dangerouslySetInnerHTML={{ __html: html }}
      />
    </div>
  );
}
