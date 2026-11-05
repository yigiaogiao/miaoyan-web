import { Maximize, Printer, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type MouseEvent } from 'react';
import type { RevealConfig } from 'reveal.js';
import { renderDiagrams } from '../lib/diagrams';
import { applySlideElements, buildDeck } from '../lib/ppt';
import { useResolvedTheme } from '../lib/theme';
import 'reveal.js/reveal.css';

interface Props {
  content: string;
  noteId: string;
  title: string;
  linkTitles?: ReadonlySet<string>;
  onWikilink?: (title: string, unresolved: boolean) => void;
  onClose: () => void;
}

const CONFIG_KEYS = new Set([
  'transition',
  'transitionSpeed',
  'backgroundTransition',
  'controls',
  'progress',
  'slideNumber',
  'showSlideNumber',
  'center',
  'loop',
  'shuffle',
  'fragments',
  'autoAnimate',
  'autoAnimateDuration',
  'autoAnimateEasing',
  'autoAnimateUnmatched',
  'viewDistance',
  'mobileViewDistance',
  'display',
  'navigationMode',
  'keyboard',
  'touch',
  'overview',
  'rtl',
  'margin',
]);

/** MiaoYan's PPT defaults, minus history/hash which would fight the SPA router. */
const DEFAULTS: Record<string, unknown> = {
  width: '100%',
  height: '100%',
  margin: 0.06,
  controls: true,
  progress: true,
  history: false,
  transition: 'slide',
  backgroundTransition: 'none',
  center: false,
  pdfSeparateFragments: false,
  slideNumber: false,
  showSlideNumber: 'all',
  hash: false,
  embedded: false,
};

export function PptView({
  content,
  noteId,
  title,
  linkTitles,
  onWikilink,
  onClose,
}: Props) {
  const theme = useResolvedTheme();
  const hostRef = useRef<HTMLDivElement>(null);
  const overlayRef = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [ready, setReady] = useState(false);

  const deck = useMemo(
    () => buildDeck(content, noteId, linkTitles),
    [content, noteId, linkTitles],
  );

  useEffect(() => {
    const host = hostRef.current;
    if (!host) {
      return;
    }

    setReady(false);
    setError(null);

    const revealRoot = document.createElement('div');
    revealRoot.className = 'reveal ppt-reveal';
    const slidesRoot = document.createElement('div');
    slidesRoot.className = 'slides';
    revealRoot.appendChild(slidesRoot);
    host.replaceChildren(revealRoot);

    for (const slide of deck.slides) {
      const section = document.createElement('section');
      section.className = 'md-preview ppt-slide';
      for (const [name, value] of Object.entries(slide.attrs)) {
        // Remote iframes are blocked by the CSP and by policy.
        if (name === 'data-background-iframe' || name === 'data-background-video-local') {
          continue;
        }
        section.setAttribute(name, value);
      }
      section.innerHTML = slide.html;
      slidesRoot.appendChild(section);
    }

    applySlideElements(slidesRoot, deck);
    for (const section of slidesRoot.querySelectorAll('section')) {
      if (section.childElementCount === 1) {
        section.classList.add('center');
      }
    }

    const diagrams = renderDiagrams(slidesRoot, theme);
    let cancelled = false;
    let controller: { initialize: () => Promise<unknown>; destroy: () => void } | null = null;

    void import('reveal.js').then(async ({ default: RevealCtor }) => {
      if (cancelled) {
        return;
      }
      const options: Record<string, unknown> = { ...DEFAULTS };
      for (const [key, value] of Object.entries(deck.config)) {
        if (CONFIG_KEYS.has(key)) {
          options[key] = value;
        }
      }

      controller = new RevealCtor(revealRoot, options as unknown as RevealConfig);
      await controller.initialize();
      if (cancelled) {
        controller.destroy();
        return;
      }
      setReady(true);
    }).catch((reason: unknown) => {
      setError(reason instanceof Error ? reason.message : '演示文稿初始化失败');
    });

    return () => {
      cancelled = true;
      diagrams.cancel();
      try {
        controller?.destroy();
      } catch {
        // Already torn down.
      }
    };
  }, [deck, theme]);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
      }
    }
    // Capture phase so the deck cannot swallow Escape as "exit overview".
    window.addEventListener('keydown', onKeyDown, true);
    return () => window.removeEventListener('keydown', onKeyDown, true);
  }, [onClose]);

  function onClick(event: MouseEvent<HTMLDivElement>) {
    const anchor = (event.target as HTMLElement).closest<HTMLAnchorElement>(
      'a[data-wikilink]',
    );
    if (!anchor) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    const linkTitle = anchor.dataset.wikilink;
    if (linkTitle) {
      onWikilink?.(linkTitle, anchor.classList.contains('wikilink-unresolved'));
    }
  }

  return (
    <div
      ref={overlayRef}
      data-ppt-open="true"
      className="ppt-overlay fixed inset-0 z-[60] flex flex-col bg-[var(--bg)]"
      role="dialog"
      aria-modal="true"
      aria-label={`演示：${title}`}
    >
      <div className="ppt-toolbar flex h-11 shrink-0 items-center gap-2 border-b border-[var(--border)] bg-[var(--surface)] px-3">
        <span className="truncate text-xs font-medium">{title}</span>
        <span className="text-[11px] text-[var(--text-faint)]">
          {deck.slides.length} 页 · {deck.separators} 个分隔符
        </span>
        <span className="flex-1" />
        <button
          type="button"
          title="全屏"
          onClick={() => {
            void overlayRef.current?.requestFullscreen?.().catch(() => undefined);
          }}
          className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
        >
          <Maximize size={15} />
        </button>
        <button
          type="button"
          title="打印 / 导出 PDF"
          onClick={() => window.print()}
          className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
        >
          <Printer size={15} />
        </button>
        <button
          type="button"
          title="退出演示 (Esc)"
          onClick={onClose}
          className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--danger-soft)] hover:text-[var(--danger)]"
        >
          <X size={16} />
        </button>
      </div>

      <div className="relative min-h-0 flex-1">
        <div ref={hostRef} onClick={onClick} className="h-full" />
        {!ready && !error && (
          <div className="pointer-events-none absolute inset-0 grid place-items-center text-sm text-[var(--text-faint)]">
            正在准备演示…
          </div>
        )}
        {error && (
          <div className="absolute inset-0 grid place-items-center p-8 text-center text-sm text-[var(--danger)]">
            {error}
          </div>
        )}
      </div>
    </div>
  );
}
