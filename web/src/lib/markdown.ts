import DOMPurify from 'dompurify';
import hljs from 'highlight.js/lib/core';
import bash from 'highlight.js/lib/languages/bash';
import c from 'highlight.js/lib/languages/c';
import cpp from 'highlight.js/lib/languages/cpp';
import csharp from 'highlight.js/lib/languages/csharp';
import css from 'highlight.js/lib/languages/css';
import diff from 'highlight.js/lib/languages/diff';
import dockerfile from 'highlight.js/lib/languages/dockerfile';
import go from 'highlight.js/lib/languages/go';
import ini from 'highlight.js/lib/languages/ini';
import java from 'highlight.js/lib/languages/java';
import javascript from 'highlight.js/lib/languages/javascript';
import json from 'highlight.js/lib/languages/json';
import kotlin from 'highlight.js/lib/languages/kotlin';
import markdown from 'highlight.js/lib/languages/markdown';
import php from 'highlight.js/lib/languages/php';
import python from 'highlight.js/lib/languages/python';
import ruby from 'highlight.js/lib/languages/ruby';
import rust from 'highlight.js/lib/languages/rust';
import shell from 'highlight.js/lib/languages/shell';
import sql from 'highlight.js/lib/languages/sql';
import swift from 'highlight.js/lib/languages/swift';
import typescript from 'highlight.js/lib/languages/typescript';
import xml from 'highlight.js/lib/languages/xml';
import yaml from 'highlight.js/lib/languages/yaml';
import katex from 'katex';
import MarkdownIt from 'markdown-it';
import type { StateBlock, StateCore, StateInline } from 'markdown-it';
import footnote from 'markdown-it-footnote';
import { assetUrl } from '../api/client';
import { t } from './i18n';
import { isAudioPath } from './utils';

const LANGUAGES: [string, Parameters<typeof hljs.registerLanguage>[1], string[]?][] = [
  ['bash', bash, ['sh', 'shell', 'zsh']],
  ['c', c],
  ['cpp', cpp, ['c++', 'cc', 'h']],
  ['csharp', csharp, ['cs', 'c#']],
  ['css', css],
  ['diff', diff, ['patch']],
  ['dockerfile', dockerfile, ['docker']],
  ['go', go, ['golang']],
  ['ini', ini, ['toml', 'conf']],
  ['java', java],
  ['javascript', javascript, ['js', 'jsx', 'mjs', 'cjs']],
  ['json', json],
  ['kotlin', kotlin, ['kt', 'kts']],
  ['markdown', markdown, ['md']],
  ['php', php],
  ['python', python, ['py']],
  ['ruby', ruby, ['rb']],
  ['rust', rust, ['rs']],
  ['shell', shell, ['console', 'terminal']],
  ['sql', sql],
  ['swift', swift],
  ['typescript', typescript, ['ts', 'tsx']],
  ['xml', xml, ['html', 'xhtml', 'svg', 'vue']],
  ['yaml', yaml, ['yml']],
];

for (const [name, language, aliases] of LANGUAGES) {
  if (aliases) {
    hljs.registerAliases(aliases, { languageName: name });
  }
  hljs.registerLanguage(name, language);
}

const md = new MarkdownIt({
  html: true,
  linkify: true,
  breaks: false,
  typographer: false,
});

md.use(footnote);

const DIAGRAM_LANGUAGES = new Set(['mermaid', 'plantuml', 'puml', 'markmap']);
const ALERT_LABELS: Record<string, string> = {
  NOTE: 'Note',
  TIP: 'Tip',
  IMPORTANT: 'Important',
  WARNING: 'Warning',
  CAUTION: 'Caution',
};

const defaultImage = md.renderer.rules.image!;

const ASSET_DESTINATION = /^(?:\.\/|\/)?(?:i|files)\//;
const FENCE_LINE = /^\s*(`{3,}|~{3,})/;

function decodePathSegments(value: string): string {
  return value
    .split('/')
    .map((segment) => {
      try {
        return decodeURIComponent(segment);
      } catch {
        return segment;
      }
    })
    .join('/');
}

/**
 * Older uploads wrote filenames such as "image 1.png" straight into the
 * Markdown destination. CommonMark does not allow unescaped spaces there, so
 * the line rendered as text. Wrap only i/ and files/ destinations in angle
 * brackets, leaving fenced and inline code untouched.
 */
function repairAssetDestinations(content: string): string {
  if (!content.includes('i/') && !content.includes('files/')) {
    return content;
  }

  const lines = content.split('\n');
  let fence: string | null = null;
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    const fenceMatch = line.match(FENCE_LINE);
    if (fenceMatch) {
      const marker = fenceMatch[1][0];
      if (fence === null) {
        fence = marker;
      } else if (fence === marker) {
        fence = null;
      }
      continue;
    }

    if (fence !== null) {
      continue;
    }

    lines[index] = line.replace(/`[^`\n]*`|!?\[[^\]]*\]\([^)\n]*\)/g, (match) => {
      if (match.startsWith('`')) {
        return match;
      }

      const destination = match.match(/^!?\[[^\]]*\]\(([^)\n]*)\)$/)?.[1];
      if (destination === undefined) {
        return match;
      }

      const trimmed = destination.trim();
      if (
        !trimmed ||
        trimmed.startsWith('<') ||
        !/\s/.test(trimmed) ||
        !ASSET_DESTINATION.test(trimmed)
      ) {
        return match;
      }

      const titleIndex = trimmed.search(/\s+["']/);
      const path = titleIndex > 0 ? trimmed.slice(0, titleIndex) : trimmed;
      const title = titleIndex > 0 ? trimmed.slice(titleIndex) : '';
      const open = match.lastIndexOf('(');
      return `${match.slice(0, open + 1)}<${path}>${title})`;
    });
  }

  return lines.join('\n');
}

function rewriteSource(source: string, noteId: string): string {
  if (/^(https?:|data:|blob:)/i.test(source)) {
    return source;
  }

  // markdown-it normalises angle-bracket destinations to percent escapes.
  // assetUrl encodes each segment again, so decode first to avoid %2520.
  const clean = decodePathSegments(source.replace(/^\.\//, '').replace(/^\//, ''));
  if (clean.startsWith('i/') || clean.startsWith('files/')) {
    return assetUrl(noteId, clean);
  }

  return source;
}

md.renderer.rules.image = (tokens, idx, options, env, self) => {
  const token = tokens[idx];
  const source = String(token.attrGet('src') ?? '');
  const noteId = typeof env?.noteId === 'string' ? env.noteId : '';

  if (!noteId || !source) {
    return defaultImage(tokens, idx, options, env, self);
  }

  const resolved = rewriteSource(source, noteId);
  if (isAudioPath(source)) {
    const label = md.utils.escapeHtml(token.content || t('markdown.audio'));
    return `<audio controls preload="metadata" src="${md.utils.escapeHtml(resolved)}" aria-label="${label}"></audio>`;
  }

  token.attrSet('src', resolved);
  token.attrSet('loading', 'lazy');
  token.attrSet('decoding', 'async');
  return defaultImage(tokens, idx, options, env, self);
};

md.renderer.rules.link_open = (tokens, idx, options, _env, self) => {
  const href = String(tokens[idx].attrGet('href') ?? '');
  if (/^https?:/i.test(href)) {
    tokens[idx].attrSet('target', '_blank');
    tokens[idx].attrSet('rel', 'noopener noreferrer nofollow');
  }
  return self.renderToken(tokens, idx, options);
};

// MiaoYan links notes by exact title inside [[double brackets]]. Alias and
// heading syntax are intentionally not supported, matching the native parser.
function wikilinkRule(state: StateInline, silent: boolean): boolean {
  const start = state.pos;
  if (state.src.charCodeAt(start) !== 0x5b || state.src.charCodeAt(start + 1) !== 0x5b) {
    return false;
  }

  const end = state.src.indexOf(']]', start + 2);
  if (end < 0) {
    return false;
  }

  const inner = state.src.slice(start + 2, end);
  const title = inner.trim();
  if (!title || /[\[\]\r\n]/.test(inner)) {
    return false;
  }

  if (!silent) {
    const token = state.push('wikilink', '', 0);
    token.content = title;
    token.meta = { title };
  }

  state.pos = end + 2;
  return true;
}

md.inline.ruler.before('link', 'wikilink', wikilinkRule);

md.renderer.rules.wikilink = (tokens, idx, _options, env) => {
  const title = tokens[idx].content;
  const titles = env?.linkTitles as Set<string> | undefined;
  const resolved = titles?.has(title.toLowerCase()) ?? true;
  const escaped = md.utils.escapeHtml(title);
  return `<a href="#" class="wikilink${resolved ? '' : ' wikilink-unresolved'}" data-wikilink="${escaped}" title="${
    resolved ? t('markdown.open', { title: escaped }) : t('markdown.create', { title: escaped })
  }">${escaped}</a>`;
};

// ---------- Math (KaTeX) ----------
// MiaoYan renders $inline$ and $$block$$. A currency heuristic keeps "$5 and
// $10" from becoming math: only digit/punctuation-only spans are rejected.

function mathInline(state: StateInline, silent: boolean): boolean {
  const start = state.pos;
  if (state.src.charCodeAt(start) !== 0x24 || state.src.charCodeAt(start + 1) === 0x24) {
    return false;
  }
  if (start > 0 && state.src.charCodeAt(start - 1) === 0x5c) {
    return false;
  }

  let end = start + 1;
  while (end < state.src.length) {
    const code = state.src.charCodeAt(end);
    if (code === 0x0a) {
      return false;
    }
    if (code === 0x24 && state.src.charCodeAt(end - 1) !== 0x5c) {
      break;
    }
    end += 1;
  }

  if (end >= state.src.length || end === start + 1) {
    return false;
  }

  const content = state.src.slice(start + 1, end);
  if (!content.trim() || /^\s|\s$/.test(content)) {
    return false;
  }
  if (/^[\d.,\s]+$/.test(content)) {
    return false;
  }

  if (!silent) {
    const token = state.push('math_inline', 'math', 0);
    token.content = content;
    token.markup = '$';
  }

  state.pos = end + 1;
  return true;
}

function mathBlock(
  state: StateBlock,
  startLine: number,
  endLine: number,
  silent: boolean,
): boolean {
  let pos = state.bMarks[startLine] + state.tShift[startLine];
  const max = state.eMarks[startLine];
  if (state.src.slice(pos, pos + 2) !== '$$') {
    return false;
  }

  if (silent) {
    return true;
  }

  let content = state.src.slice(pos + 2, max);
  let nextLine = startLine;
  let found = false;

  if (content.trim().endsWith('$$')) {
    content = content.trim().slice(0, -2);
    found = true;
  } else {
    for (nextLine = startLine + 1; nextLine < endLine; nextLine += 1) {
      pos = state.bMarks[nextLine] + state.tShift[nextLine];
      const lineMax = state.eMarks[nextLine];
      const line = state.src.slice(pos, lineMax);
      if (line.trim().endsWith('$$')) {
        content += `\n${line.trim().slice(0, -2)}`;
        found = true;
        break;
      }
      content += `\n${line}`;
    }
  }

  if (!found) {
    return false;
  }

  const token = state.push('math_block', 'math', 0);
  token.block = true;
  token.content = content.replace(/^\n+/, '').replace(/\n+$/, '');
  token.map = [startLine, nextLine + 1];
  token.markup = '$$';
  state.line = nextLine + 1;
  return true;
}

md.inline.ruler.before('escape', 'math_inline', mathInline);
md.block.ruler.before('fence', 'math_block', mathBlock, {
  alt: ['paragraph', 'reference', 'blockquote', 'list'],
});

md.renderer.rules.math_inline = (tokens, idx) =>
  katex.renderToString(tokens[idx].content, {
    throwOnError: false,
    displayMode: false,
    strict: false,
    trust: false,
  });

md.renderer.rules.math_block = (tokens, idx) => {
  const rendered = katex.renderToString(tokens[idx].content, {
    throwOnError: false,
    displayMode: true,
    strict: false,
    trust: false,
  });
  return `<div class="math-block">${rendered}</div>\n`;
};

// ---------- Code fences ----------
// Mermaid / PlantUML / Markmap keep their source in a hidden <pre>; the
// preview mounts the rendered SVG after sanitisation. Everything else is
// highlighted locally by highlight.js.

md.renderer.rules.fence = (tokens, idx, _options, env) => {
  const token = tokens[idx];
  const info = token.info.trim().split(/\s+/)[0]?.toLowerCase() ?? '';
  const code = token.content;
  const escaped = md.utils.escapeHtml(code);
  const line = token.map?.[0] ?? 0;
  const skipHighlight = Boolean((env as { skipHighlight?: boolean } | undefined)?.skipHighlight);

  if (DIAGRAM_LANGUAGES.has(info)) {
    const kind = info === 'puml' ? 'plantuml' : info;
    return `<figure class="diagram" data-diagram="${kind}" data-line="${line}"><pre class="diagram-source" aria-hidden="true">${escaped}</pre><div class="diagram-canvas"></div></figure>\n`;
  }

  const language = info && hljs.getLanguage(info) ? info : '';
  const highlighted = language && !skipHighlight
    ? hljs.highlight(code, { language, ignoreIllegals: true }).value
    : escaped;
  return `<pre class="code-block" data-line="${line}"><code class="hljs${
    language ? ` language-${md.utils.escapeHtml(language)}` : ''
  }">${highlighted}</code></pre>\n`;
};

// ---------- GitHub alerts ----------
// A blockquote whose first paragraph starts with [!NOTE] becomes an alert.
// The label is a CSS ::before fed by data-alert-label, so no inline HTML is
// needed inside the quote.

md.core.ruler.after('inline', 'alerts', (state: StateCore) => {
  const tokens = state.tokens;
  for (let index = 0; index < tokens.length; index += 1) {
    const open = tokens[index];
    if (open.type !== 'blockquote_open') {
      continue;
    }

    let inline: (typeof tokens)[number] | undefined;
    for (let cursor = index + 1; cursor < tokens.length; cursor += 1) {
      if (tokens[cursor].type === 'blockquote_close') {
        break;
      }
      if (tokens[cursor].type === 'inline') {
        inline = tokens[cursor];
        break;
      }
    }
    if (!inline) {
      continue;
    }

    const match = /^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\][ \t]*(?:\n|$)/i.exec(
      inline.content,
    );
    if (!match) {
      continue;
    }

    const type = match[1].toUpperCase();
    open.attrJoin('class', `alert alert-${type.toLowerCase()}`);
    open.attrSet('data-alert-label', ALERT_LABELS[type]);
    inline.content = inline.content.slice(match[0].length);
    const firstText = inline.children?.find((child) => child.type === 'text');
    if (firstText) {
      firstText.content = firstText.content.replace(match[0], '');
    }
    // An alert marker on its own line leaves an empty first paragraph.
    const paragraph = tokens[index + 1];
    if (
      paragraph?.type === 'paragraph_open' &&
      inline.content.trim() === '' &&
      tokens[index + 2]?.type === 'paragraph_close'
    ) {
      tokens.splice(index + 1, 2);
    }
  }
});

// ---------- Anchors and source lines ----------
// Headings get stable ids for the TOC; top-level blocks carry data-line so the
// split view can scroll the editor and preview together.

function slugify(text: string): string {
  return text
    .toLowerCase()
    .replace(/[`*_~]/g, '')
    .replace(/[^\p{L}\p{N}\s-]/gu, '')
    .trim()
    .replace(/\s+/g, '-');
}

md.core.ruler.after('inline', 'anchors', (state: StateCore) => {
  const seen = new Map<string, number>();
  const tokens = state.tokens;

  for (let index = 0; index < tokens.length; index += 1) {
    const token = tokens[index];
    if (token.map && token.level === 0 && token.type.endsWith('_open')) {
      token.attrSet('data-line', String(token.map[0]));
    }

    if (token.type !== 'heading_open') {
      continue;
    }

    const inline = tokens[index + 1];
    const text =
      inline?.children
        ?.filter((child) => child.type === 'text' || child.type === 'code_inline')
        .map((child) => child.content)
        .join('') ?? '';
    const base = slugify(text) || 'section';
    const count = seen.get(base) ?? 0;
    seen.set(base, count + 1);
    token.attrSet('id', count === 0 ? base : `${base}-${count + 1}`);
  }
});

export function stripFrontmatter(text: string): string {
  if (!text.startsWith('---')) {
    return text;
  }

  const match = /^---\r?\n([\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)/.exec(text);
  if (!match) {
    return text;
  }

  const lines = match[1].split(/\r?\n/).filter((line) => line.trim().length > 0);
  if (lines.length === 0) {
    return text;
  }

  const looksLikeYaml = lines.every(
    (line) => /^[\w.-]+\s*:/.test(line) || /^\s+/.test(line) || /^-\s/.test(line) || line.startsWith('#'),
  );

  return looksLikeYaml ? text.slice(match[0].length) : text;
}

// Renders GFM-style task list markers as disabled checkboxes.
md.core.ruler.after('inline', 'task-lists', (state) => {
  const tokens = state.tokens;
  for (let index = 0; index < tokens.length; index += 1) {
    const token = tokens[index];
    if (token.type !== 'inline' || !token.children) {
      continue;
    }

    const marker = /^\[([ xX])\]\s+/.exec(token.content);
    if (!marker) {
      continue;
    }

    let itemIndex = index - 1;
    while (itemIndex >= 0 && tokens[itemIndex].type !== 'list_item_open') {
      itemIndex -= 1;
    }
    if (itemIndex < 0) {
      continue;
    }

    tokens[itemIndex].attrJoin('class', 'task-list-item');

    const firstText = token.children.find((child) => child.type === 'text');
    if (!firstText) {
      continue;
    }

    firstText.content = firstText.content.replace(/^\[([ xX])\]\s+/, '');
    const checkbox = new state.Token('html_inline', '', 0);
    checkbox.content = `<input type="checkbox" disabled${marker[1].toLowerCase() === 'x' ? ' checked' : ''}>`;
    token.children.unshift(checkbox);
  }
});

function sanitize(html: string): string {
  return DOMPurify.sanitize(html, {
    ADD_TAGS: ['audio', 'video', 'source'],
    ADD_ATTR: [
      'target',
      'rel',
      'loading',
      'decoding',
      'controls',
      'preload',
      'loop',
      'autoplay',
      'muted',
      'width',
      'height',
      'poster',
      'type',
      'aria-label',
      'aria-hidden',
      'data-wikilink',
      'data-diagram',
      'data-line',
      'data-alert-label',
      'hidden',
      'id',
    ],
    FORBID_TAGS: ['style', 'iframe', 'object', 'embed', 'form', 'script'],
    FORBID_ATTR: ['onerror', 'onload', 'onclick'],
  });
}

/**
 * Resolves raw `<img>`, `<audio>`, `<video>` and `<source>` URLs authored as
 * i/ or files/ paths. Markdown images are handled by the renderer; this covers
 * the HTML templates from `/img` and `/video`, which MiaoYan also supports.
 */
function rewriteEmbeddedSources(html: string, noteId: string): string {
  if (!noteId) {
    return html;
  }

  const template = document.createElement('template');
  template.innerHTML = html;
  for (const element of template.content.querySelectorAll<HTMLElement>(
    'img[src], audio[src], video[src], source[src]',
  )) {
    const source = element.getAttribute('src');
    if (!source) {
      continue;
    }
    element.setAttribute('src', rewriteSource(source, noteId));
    if (element.tagName === 'IMG') {
      element.setAttribute('loading', 'lazy');
      element.setAttribute('decoding', 'async');
    }
  }
  return template.innerHTML;
}

export function renderMarkdown(
  content: string,
  noteId: string,
  linkTitles?: ReadonlySet<string>,
  options?: { skipHighlight?: boolean },
): string {
  const html = md.render(repairAssetDestinations(stripFrontmatter(content)), {
    noteId,
    skipHighlight: options?.skipHighlight,
    linkTitles: linkTitles ? new Set([...linkTitles].map((title) => title.toLowerCase())) : undefined,
  });
  return rewriteEmbeddedSources(sanitize(html), noteId);
}

/**
 * PPT slides may contain raw HTML for multi-column layouts. The renderer is
 * shared but temporarily allows HTML, and the output still goes through the
 * same DOMPurify pass as normal previews.
 */
export function renderMarkdownDocument(
  content: string,
  noteId: string,
  linkTitles?: ReadonlySet<string>,
): string {
  const previous = md.options.html;
  md.options.html = true;
  try {
    const html = md.render(repairAssetDestinations(content), {
      noteId,
      linkTitles: linkTitles
        ? new Set([...linkTitles].map((title) => title.toLowerCase()))
        : undefined,
    });
    return sanitize(html);
  } finally {
    md.options.html = previous;
  }
}

export interface HeadingEntry {
  id: string;
  text: string;
  level: number;
  line: number;
}

/** Headings outside fenced code, used by the outline drawer. */
export function extractHeadings(content: string): HeadingEntry[] {
  const headings: HeadingEntry[] = [];
  const seen = new Map<string, number>();
  let fence: string | null = null;

  content.split(/\r?\n/).forEach((raw, index) => {
    const line = raw.trim();
    const fenceMatch = /^(```+|~~~+)/.exec(line);
    if (fenceMatch) {
      if (fence === null) {
        fence = fenceMatch[1][0];
      } else if (fenceMatch[1][0] === fence) {
        fence = null;
      }
      return;
    }
    if (fence !== null) {
      return;
    }

    const match = /^(#{1,6})\s+(.+?)\s*#*\s*$/.exec(line);
    if (!match) {
      return;
    }

    const level = match[1].length;
    const text = match[2].replace(/[*_`~]/g, '');
    const base = slugify(text) || 'section';
    const count = seen.get(base) ?? 0;
    seen.set(base, count + 1);
    headings.push({
      id: count === 0 ? base : `${base}-${count + 1}`,
      text,
      level,
      line: index,
    });
  });

  return headings;
}
