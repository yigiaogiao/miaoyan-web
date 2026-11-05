import DOMPurify from 'dompurify';
import MarkdownIt from 'markdown-it';
import type { StateInline } from 'markdown-it';
import { assetUrl } from '../api/client';
import { isAudioPath } from './utils';

const md = new MarkdownIt({
  html: false,
  linkify: true,
  breaks: false,
  typographer: false,
});

const defaultImage = md.renderer.rules.image!;

function rewriteSource(source: string, noteId: string): string {
  if (/^(https?:|data:|blob:)/i.test(source)) {
    return source;
  }

  const clean = source.replace(/^\.\//, '').replace(/^\//, '');
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
    const label = md.utils.escapeHtml(token.content || '音频');
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
    resolved ? `打开 ${escaped}` : `创建 ${escaped}`
  }">${escaped}</a>`;
};

function stripFrontmatter(text: string): string {
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

export function renderMarkdown(
  content: string,
  noteId: string,
  linkTitles?: ReadonlySet<string>,
): string {
  const html = md.render(stripFrontmatter(content), {
    noteId,
    linkTitles: linkTitles ? new Set([...linkTitles].map((title) => title.toLowerCase())) : undefined,
  });
  return DOMPurify.sanitize(html, {
    ADD_TAGS: ['audio'],
    ADD_ATTR: [
      'target',
      'rel',
      'loading',
      'decoding',
      'controls',
      'preload',
      'aria-label',
      'data-wikilink',
    ],
    FORBID_TAGS: ['style', 'iframe', 'object', 'embed', 'form'],
    FORBID_ATTR: ['onerror', 'onload', 'onclick'],
  });
}
