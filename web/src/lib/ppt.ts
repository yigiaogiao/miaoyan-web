import { renderMarkdownDocument, stripFrontmatter } from './markdown';

export type PptConfig = Record<string, unknown>;

export interface PptSlide {
  html: string;
  attrs: Record<string, string>;
  /** Marker id -> attributes from `<!-- .element: ... -->` comments. */
  fragments: Record<string, Record<string, string>>;
}

export interface PptDeck {
  config: PptConfig;
  slides: PptSlide[];
  /** Number of `---` separators outside code fences. */
  separators: number;
}

const SLIDE_ATTR = /<!--\s*\.slide:\s*([\s\S]*?)-->/g;
const ELEMENT_ATTR = /<!--\s*\.element:\s*([\s\S]*?)-->/g;
const CONFIG_LINE = /^([\w.-]+)\s*:\s*(.*)$/;
const SEPARATOR = /^\s*-{3,}\s*$/;
const FENCE = /^\s*(`{3,}|~{3,})/;

// Private-use delimiters keep fragment markers from ever matching note text.
const MARKER_PREFIX = String.fromCharCode(0xe000);
const MARKER_SUFFIX = String.fromCharCode(0xe001);
const MARKER = new RegExp(`${MARKER_PREFIX}(\\d+)${MARKER_SUFFIX}`, 'g');

/**
 * Parses the leading `<!-- key: value -->` comment exactly like MiaoYan:
 * dot-nested keys, bracketed lists, booleans, numbers, null and quoted strings.
 */
export function parsePptConfig(source: string): { config: PptConfig; rest: string } {
  const trimmed = source.replace(/^\s+/, '');
  if (!trimmed.startsWith('<!--')) {
    return { config: {}, rest: source };
  }

  const end = trimmed.indexOf('-->');
  if (end === -1) {
    return { config: {}, rest: source };
  }

  const config: PptConfig = {};
  for (const rawLine of trimmed.slice(4, end).split(/\r?\n/)) {
    const line = rawLine.trim();
    const match = CONFIG_LINE.exec(line);
    if (!match) {
      continue;
    }

    const key = match[1];
    let value = match[2].trim();

    if (value.startsWith('[') && value.endsWith(']')) {
      assign(
        config,
        key,
        value
          .slice(1, -1)
          .split(',')
          .map((item) => item.trim())
          .filter(Boolean),
      );
      continue;
    }

    if (
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))
    ) {
      value = value.slice(1, -1);
    }

    if (/^null$/i.test(value)) {
      assign(config, key, null);
    } else if (/^undefined$/i.test(value)) {
      assign(config, key, undefined);
    } else if (/^(true|false)$/i.test(value)) {
      assign(config, key, /^true$/i.test(value));
    } else if (value !== '' && !Number.isNaN(Number(value))) {
      assign(config, key, Number(value));
    } else {
      assign(config, key, value);
    }
  }

  return { config, rest: trimmed.slice(end + 3).replace(/^\s+/, '') };
}

function assign(target: PptConfig, path: string, value: unknown): void {
  const segments = path.split('.').filter(Boolean);
  let current = target;
  segments.forEach((segment, index) => {
    if (index === segments.length - 1) {
      current[segment] = value;
      return;
    }
    const next = current[segment];
    if (!next || typeof next !== 'object' || Array.isArray(next)) {
      current[segment] = {};
    }
    current = current[segment] as PptConfig;
  });
}

export function parseAttributeString(source: string): Record<string, string> {
  const attrs: Record<string, string> = {};
  const pattern = /([\w-]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"']+)))?/g;
  for (const match of source.matchAll(pattern)) {
    attrs[match[1]] = match[2] ?? match[3] ?? match[4] ?? '';
  }
  return attrs;
}

/** Splits on `---` lines outside fenced code, like reveal's markdown plugin. */
export function splitSlides(source: string): string[] {
  const slides: string[] = [];
  let current: string[] = [];
  let fence: string | null = null;

  for (const line of source.split(/\r?\n/)) {
    const fenceMatch = FENCE.exec(line);
    if (fenceMatch) {
      if (fence === null) {
        fence = fenceMatch[1][0];
      } else if (fenceMatch[1][0] === fence) {
        fence = null;
      }
    }

    if (fence === null && SEPARATOR.test(line)) {
      slides.push(current.join('\n'));
      current = [];
      continue;
    }
    current.push(line);
  }

  slides.push(current.join('\n'));
  return slides;
}

export function buildDeck(
  content: string,
  noteId: string,
  linkTitles?: ReadonlySet<string>,
): PptDeck {
  const withoutFrontmatter = stripFrontmatter(content);
  const { config, rest } = parsePptConfig(withoutFrontmatter);
  const chunks = splitSlides(rest);

  let markerIndex = 0;
  const slides = chunks.map((chunk) => {
    const attrs: Record<string, string> = {};
    for (const match of chunk.matchAll(SLIDE_ATTR)) {
      Object.assign(attrs, parseAttributeString(match[1]));
    }

    const fragments: Record<string, Record<string, string>> = {};
    let body = chunk.replace(SLIDE_ATTR, '');
    body = body.replace(ELEMENT_ATTR, (_full, source: string) => {
      const marker = `${MARKER_PREFIX}${markerIndex}${MARKER_SUFFIX}`;
      fragments[marker] = parseAttributeString(source);
      markerIndex += 1;
      return marker;
    });

    return {
      html: renderMarkdownDocument(body.trim(), noteId, linkTitles),
      attrs,
      fragments,
    };
  });

  return {
    config,
    slides,
    separators: chunks.length - 1,
  };
}

/**
 * Applies `.element:` fragments to the DOM after the sanitised slide HTML has
 * been mounted. Returns the number of fragment elements touched.
 */
export function applySlideElements(root: HTMLElement, deck: PptDeck): number {
  const fragments = new Map<string, Record<string, string>>();
  for (const slide of deck.slides) {
    for (const [marker, attrs] of Object.entries(slide.fragments)) {
      fragments.set(marker, attrs);
    }
  }

  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const pending: { attrs: Record<string, string>; target: HTMLElement; node: Text }[] = [];
  let node = walker.nextNode();

  while (node) {
    const text = node as Text;
    MARKER.lastIndex = 0;
    if (MARKER.test(text.data)) {
      MARKER.lastIndex = 0;
      let match: RegExpExecArray | null;
      while ((match = MARKER.exec(text.data)) !== null) {
        const attrs = fragments.get(match[0]);
        const parent = text.parentElement;
        if (!attrs || !parent) {
          continue;
        }

        let target: HTMLElement | null = parent;
        if (
          parent.tagName === 'P' &&
          parent.textContent?.replace(MARKER, '').trim() === ''
        ) {
          target = parent.previousElementSibling as HTMLElement | null;
          parent.remove();
        } else if (parent.closest('section') === parent) {
          target = null;
        }

        if (target) {
          pending.push({ attrs, target, node: text });
        }
      }
    }
    node = walker.nextNode();
  }

  for (const fragment of pending) {
    for (const [key, value] of Object.entries(fragment.attrs)) {
      fragment.target.setAttribute(key, value);
    }
    fragment.node.data = fragment.node.data.replace(MARKER, '');
  }

  root.querySelectorAll('p').forEach((paragraph) => {
    if (!paragraph.textContent?.trim() && !paragraph.children.length) {
      paragraph.remove();
    }
  });

  return pending.length;
}
