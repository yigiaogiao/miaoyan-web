import { X } from 'lucide-react';
import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
  type MouseEvent,
  type Ref,
} from 'react';
import { renderDiagrams } from '../lib/diagrams';
import { useI18n } from '../lib/i18n';
import { renderMarkdown } from '../lib/markdown';
import { useResolvedTheme } from '../lib/theme';

interface Props {
  content: string;
  noteId: string;
  linkTitles?: ReadonlySet<string>;
  onWikilink?: (title: string, unresolved: boolean) => void;
  onVisibleLine?: (line: number) => void;
}

export interface MarkdownPreviewHandle {
  scrollToLine: (line: number) => void;
  scrollToHeading: (id: string) => void;
}

export const MarkdownPreview = forwardRef(function MarkdownPreview(
  { content, noteId, linkTitles, onWikilink, onVisibleLine }: Props,
  ref: Ref<MarkdownPreviewHandle>,
) {
  const { t } = useI18n();
  const scrollerRef = useRef<HTMLDivElement>(null);
  const articleRef = useRef<HTMLElement>(null);
  const theme = useResolvedTheme();
  const [lightbox, setLightbox] = useState<{ src: string; alt: string } | null>(null);

  // Above 1 MB, skip highlight.js and automatic diagram rendering: parsing
  // the note is unavoidable, but highlighting every fence is not.
  const largeNote = content.length > 1_000_000;

  const html = useMemo(
    () => renderMarkdown(content, noteId, linkTitles, { skipHighlight: largeNote }),
    [content, linkTitles, largeNote, noteId],
  );

  useImperativeHandle(ref, () => ({
    scrollToLine(line) {
      const scroller = scrollerRef.current;
      const article = articleRef.current;
      if (!scroller || !article) {
        return;
      }
      const blocks = article.querySelectorAll<HTMLElement>('[data-line]');
      let target: HTMLElement | null = null;
      for (const block of blocks) {
        if (Number(block.dataset.line) <= line) {
          target = block;
        } else {
          break;
        }
      }
      if (!target) {
        target = blocks[0] ?? null;
      }
      if (target) {
        const delta =
          target.getBoundingClientRect().top - scroller.getBoundingClientRect().top;
        scroller.scrollTop += delta - 12;
      }
    },
    scrollToHeading(id) {
      const scroller = scrollerRef.current;
      const article = articleRef.current;
      const heading = article?.querySelector<HTMLElement>(`#${CSS.escape(id)}`);
      if (!scroller || !heading) {
        return;
      }
      const delta =
        heading.getBoundingClientRect().top - scroller.getBoundingClientRect().top;
      scroller.scrollTop += delta - 16;
    },
  }));

  // Diagrams render after sanitisation: mermaid/markmap draw their own SVG and
  // PlantUML comes back as server-rendered SVG (local jar, never a public
  // service).
  useEffect(() => {
    const article = articleRef.current;
    if (!article || largeNote) {
      return;
    }
    const handles = renderDiagrams(article, theme);
    return () => handles.cancel();
  }, [html, largeNote, theme]);

  // Copy buttons on code blocks.
  useEffect(() => {
    const article = articleRef.current;
    if (!article) {
      return;
    }
    const cleanups: (() => void)[] = [];
    article.querySelectorAll<HTMLPreElement>('pre.code-block').forEach((pre) => {
      if (pre.querySelector('.code-copy')) {
        return;
      }
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'code-copy';
      button.textContent = t('markdown.copy');
      const onClick = (event: globalThis.MouseEvent) => {
        event.preventDefault();
        const text = pre.querySelector('code')?.textContent ?? '';
        void navigator.clipboard.writeText(text).then(() => {
          button.textContent = t('markdown.copied');
          window.setTimeout(() => {
            button.textContent = t('markdown.copy');
          }, 1200);
        });
      };
      button.addEventListener('click', onClick);
      pre.appendChild(button);
      cleanups.push(() => button.removeEventListener('click', onClick));
    });
    return () => cleanups.forEach((cleanup) => cleanup());
  }, [html, t]);

  useEffect(() => {
    if (!lightbox) {
      return;
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setLightbox(null);
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [lightbox]);

  function onClick(event: MouseEvent<HTMLElement>) {
    const target = event.target as HTMLElement;
    const anchor = target.closest<HTMLAnchorElement>('a[data-wikilink]');
    if (anchor) {
      event.preventDefault();
      const title = anchor.dataset.wikilink;
      if (title) {
        onWikilink?.(title, anchor.classList.contains('wikilink-unresolved'));
      }
      return;
    }

    const image = target.closest<HTMLImageElement>('article img');
    if (image && !target.closest('a')) {
      event.preventDefault();
      setLightbox({ src: image.currentSrc || image.src, alt: image.alt });
    }
  }

  function onScroll() {
    if (!onVisibleLine) {
      return;
    }
    const scroller = scrollerRef.current;
    const article = articleRef.current;
    if (!scroller || !article) {
      return;
    }
    const top = scroller.scrollTop + 28;
    let line = 0;
    for (const block of article.querySelectorAll<HTMLElement>('[data-line]')) {
      if (block.offsetTop <= top) {
        line = Number(block.dataset.line);
      } else {
        break;
      }
    }
    onVisibleLine(line);
  }

  if (!content.trim()) {
    return (
      <div className="grid h-full place-items-center px-8 text-center text-sm text-[var(--text-faint)]">
        {t('markdown.previewEmpty')}
      </div>
    );
  }

  return (
    <div ref={scrollerRef} onScroll={onScroll} className="h-full overflow-y-auto">
      <article
        ref={articleRef}
        className="md-preview mx-auto w-full max-w-[64rem] px-7 py-12 sm:px-10 sm:py-16"
        onClick={onClick}
        dangerouslySetInnerHTML={{ __html: html }}
      />

      {lightbox && (
        <button
          type="button"
          aria-label={t('markdown.closePreview')}
          onClick={() => setLightbox(null)}
          className="fixed inset-0 z-50 grid place-items-center bg-black/70 p-6 backdrop-blur-sm"
        >
          <span className="absolute right-4 top-4 rounded-full bg-black/50 p-2 text-white">
            <X size={18} />
          </span>
          <img
            src={lightbox.src}
            alt={lightbox.alt}
            className="max-h-full max-w-full rounded-lg object-contain shadow-2xl"
            onClick={(event) => event.stopPropagation()}
          />
        </button>
      )}
    </div>
  );
});
