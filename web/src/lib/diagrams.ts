import DOMPurify from 'dompurify';
import { t } from './i18n';

const plantUmlCache = new Map<string, string>();
let mermaidCounter = 0;

let mermaidPromise: Promise<typeof import('mermaid')['default']> | null = null;
function loadMermaid() {
  mermaidPromise ??= import('mermaid').then((module) => module.default);
  return mermaidPromise;
}

async function renderMermaid(
  canvas: HTMLElement,
  source: string,
  theme: 'light' | 'dark',
): Promise<void> {
  const mermaid = await loadMermaid();
  mermaid.initialize({
    startOnLoad: false,
    securityLevel: 'strict',
    theme: theme === 'dark' ? 'dark' : 'default',
    fontFamily: 'var(--font-sans)',
    flowchart: { htmlLabels: false },
    suppressErrorRendering: true,
  });
  mermaidCounter += 1;
  const id = `mermaid-render-${mermaidCounter}`;
  const { svg } = await mermaid.render(id, source);
  canvas.innerHTML = svg;
}

async function renderPlantUml(canvas: HTMLElement, source: string): Promise<void> {
  const cached = plantUmlCache.get(source);
  if (cached) {
    canvas.innerHTML = cached;
    return;
  }

  const response = await fetch('/api/render/plantuml', {
    method: 'POST',
    headers: { 'Content-Type': 'text/plain; charset=utf-8' },
    body: source,
  });

  if (!response.ok) {
    let message = t('diagram.plantumlFailed');
    try {
      const body = (await response.json()) as { message?: string };
      if (body.message) {
        message = body.message;
      }
    } catch {
      // Keep the generic message.
    }
    throw new Error(message);
  }

  const svg = await response.text();
  const clean = DOMPurify.sanitize(svg, {
    USE_PROFILES: { svg: true, svgFilters: true },
    FORBID_TAGS: ['script', 'foreignObject'],
  });
  plantUmlCache.set(source, clean);
  canvas.innerHTML = clean;
}

async function renderMarkmap(
  canvas: HTMLElement,
  source: string,
  theme: 'light' | 'dark',
  instances: { destroy: () => void }[],
): Promise<void> {
  const [{ Transformer }, { Markmap }] = await Promise.all([
    import('markmap-lib'),
    import('markmap-view'),
  ]);
  const { root } = new Transformer().transform(source);
  canvas.innerHTML = '';
  // markmap-view themes its text through `.markmap-dark .markmap`.
  canvas.classList.toggle('markmap-dark', theme === 'dark');
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', 'markmap-svg');
  canvas.appendChild(svg);
  const markmap = Markmap.create(svg, { autoFit: true, duration: 0 }, root);
  instances.push(markmap);
}

export interface DiagramCleanup {
  cancel: () => void;
  done: Promise<void>;
}

/**
 * Renders every figure[data-diagram] under root. Called after the sanitised
 * HTML is in the DOM; diagram markup is never produced by the sanitiser.
 */
export function renderDiagrams(
  root: HTMLElement,
  theme: 'light' | 'dark',
): DiagramCleanup {
  let cancelled = false;
  const markmaps: { destroy: () => void }[] = [];

  const done = (async () => {
    const figures = Array.from(root.querySelectorAll<HTMLElement>('figure[data-diagram]'));
    for (const figure of figures) {
      if (cancelled) {
        return;
      }
      const kind = figure.dataset.diagram;
      const canvas = figure.querySelector<HTMLElement>('.diagram-canvas');
      const source = figure.querySelector<HTMLElement>('.diagram-source')?.textContent ?? '';
      if (!canvas) {
        continue;
      }
      canvas.innerHTML = '';
      canvas.classList.remove('diagram-error');
      try {
        if (kind === 'mermaid') {
          // An empty block is normal while editing; Mermaid would report
          // "No diagram type detected", which reads like an app error.
          if (source.trim() === '') {
            canvas.textContent = t('diagram.mermaidEmpty');
          } else {
            await renderMermaid(canvas, source, theme);
          }
        } else if (kind === 'plantuml') {
          await renderPlantUml(canvas, source);
        } else if (kind === 'markmap') {
          await renderMarkmap(canvas, source, theme, markmaps);
        }
      } catch (reason) {
        if (cancelled) {
          return;
        }
        canvas.classList.add('diagram-error');
        canvas.textContent = reason instanceof Error ? reason.message : t('diagram.failed');
      }
    }
  })();

  return {
    cancel: () => {
      cancelled = true;
      for (const markmap of markmaps) {
        try {
          markmap.destroy();
        } catch {
          // The DOM is already gone.
        }
      }
    },
    done,
  };
}
