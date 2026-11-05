/**
 * Pure-string typography cleanup for CJK/Latin mixed Markdown, ported from
 * MiaoYan's TypographyCleaner: pangu spacing, fullwidth punctuation, stray em
 * dashes, ASCII ellipses and blank-line runs. Code, math, link targets,
 * wikilinks, URLs, frontmatter and hard line breaks stay untouched.
 */

interface Segment {
  text: string;
  protected: boolean;
  inlineCode: boolean;
}

interface Fence {
  marker: string;
  length: number;
}

const FULLWIDTH_PUNCTUATION = new Set(['，', '。', '！', '？', '；', '：', '、']);
const FULLWIDTH_OPENERS = new Set(['「', '『', '【', '《', '（', '“']);
const FULLWIDTH_CLOSERS = new Set(['」', '』', '】', '》', '）', '”']);
const PUNCTUATION_MAP: Record<string, string> = {
  ',': '，',
  ';': '；',
  ':': '：',
  '!': '！',
  '?': '？',
  '.': '。',
};

function isCjk(char: string | undefined): boolean {
  if (!char) {
    return false;
  }
  const code = char.codePointAt(0) ?? 0;
  return (
    (code >= 0x4e00 && code <= 0x9fff) ||
    (code >= 0x3400 && code <= 0x4dbf) ||
    (code >= 0xf900 && code <= 0xfaff) ||
    (code >= 0x3040 && code <= 0x30ff) ||
    (code >= 0x31f0 && code <= 0x31ff)
  );
}

function isLatinOrDigit(char: string | undefined): boolean {
  return !!char && /^[A-Za-z0-9]$/.test(char);
}

function unquoted(trimmed: string): string {
  let value = trimmed;
  while (value.startsWith('>')) {
    value = value.slice(1);
    while (value.startsWith(' ')) {
      value = value.slice(1);
    }
  }
  return value;
}

function openingFence(trimmed: string): Fence | null {
  const marker = trimmed[0];
  if (marker !== '`' && marker !== '~') {
    return null;
  }
  let length = 0;
  while (trimmed[length] === marker) {
    length += 1;
  }
  return length >= 3 ? { marker, length } : null;
}

function isFenceLine(trimmed: string, marker: string, minLength: number, closing: boolean): boolean {
  if (trimmed[0] !== marker) {
    return false;
  }
  let length = 0;
  while (trimmed[length] === marker) {
    length += 1;
  }
  if (length < minLength) {
    return false;
  }
  return !closing || trimmed.slice(length).trim().length === 0;
}

function doubleDollarCount(value: string): number {
  let count = 0;
  let previousWasDollar = false;
  for (const char of Array.from(value)) {
    if (char === '$') {
      if (previousWasDollar) {
        count += 1;
        previousWasDollar = false;
      } else {
        previousWasDollar = true;
      }
    } else {
      previousWasDollar = false;
    }
  }
  return count;
}

function isReferenceDefinition(trimmed: string): boolean {
  if (trimmed[0] !== '[') {
    return false;
  }
  const close = trimmed.indexOf(']');
  return close >= 0 && trimmed[close + 1] === ':';
}

function findBacktickRun(chars: string[], from: number, length: number): number | null {
  let index = from;
  while (index < chars.length) {
    if (chars[index] !== '`') {
      index += 1;
      continue;
    }
    let runEnd = index;
    while (chars[runEnd] === '`') {
      runEnd += 1;
    }
    if (runEnd - index === length) {
      return runEnd;
    }
    index = runEnd;
  }
  return null;
}

function findDoubleClose(chars: string[], from: number): number | null {
  for (let index = from; index + 1 < chars.length; index += 1) {
    if (chars[index] === ']' && chars[index + 1] === ']') {
      return index;
    }
  }
  return null;
}

function isValidMathSpan(chars: string[], open: number, close: number): boolean {
  if (close <= open + 1) {
    return false;
  }
  const inner = chars.slice(open + 1, close);
  if (inner.includes('`') || inner.some(isCjk)) {
    return false;
  }
  if (chars[open + 1] === ' ' || chars[close - 1] === ' ') {
    return false;
  }
  if (close + 1 < chars.length && /^[0-9]$/.test(chars[close + 1])) {
    return false;
  }
  return true;
}

function matchesUrlPrefix(chars: string[], index: number): boolean {
  return (
    chars.slice(index, index + 8).join('') === 'https://' ||
    chars.slice(index, index + 7).join('') === 'http://'
  );
}

function isUrlBoundary(chars: string[], index: number): boolean {
  const char = chars[index];
  if (/\s/.test(char) || [')', '>', ']', '"', "'"].includes(char)) {
    return true;
  }
  if (
    FULLWIDTH_PUNCTUATION.has(char) ||
    FULLWIDTH_OPENERS.has(char) ||
    FULLWIDTH_CLOSERS.has(char)
  ) {
    return true;
  }
  if ((char === ',' || char === ';') && index + 1 < chars.length && isCjk(chars[index + 1])) {
    return true;
  }
  return false;
}

function parseSegments(line: string): Segment[] {
  const segments: Segment[] = [];
  const chars = Array.from(line);
  let plainStart = 0;
  let index = 0;
  let noWikilinkClose = false;

  function flushPlain(upTo: number) {
    if (upTo > plainStart) {
      segments.push({ text: chars.slice(plainStart, upTo).join(''), protected: false, inlineCode: false });
    }
  }

  while (index < chars.length) {
    const char = chars[index];

    if (char === '`') {
      let runEnd = index;
      while (chars[runEnd] === '`') {
        runEnd += 1;
      }
      const close = findBacktickRun(chars, runEnd, runEnd - index);
      if (close !== null) {
        flushPlain(index);
        segments.push({ text: chars.slice(index, close).join(''), protected: true, inlineCode: true });
        plainStart = close;
        index = close;
        continue;
      }
    }

    if (char === '$') {
      const close = chars.indexOf('$', index + 1);
      if (close >= 0 && isValidMathSpan(chars, index, close)) {
        flushPlain(index);
        segments.push({ text: chars.slice(index, close + 1).join(''), protected: true, inlineCode: false });
        plainStart = close + 1;
        index = close + 1;
        continue;
      }
    }

    if (char === '[' && chars[index + 1] === '[' && !noWikilinkClose) {
      const close = findDoubleClose(chars, index + 2);
      if (close !== null) {
        flushPlain(index);
        segments.push({ text: chars.slice(index, close + 2).join(''), protected: true, inlineCode: false });
        plainStart = close + 2;
        index = close + 2;
        continue;
      }
      noWikilinkClose = true;
    }

    if (char === ']' && chars[index + 1] === '(') {
      let depth = 1;
      let cursor = index + 2;
      while (cursor < chars.length && depth > 0) {
        if (chars[cursor] === '(') {
          depth += 1;
        }
        if (chars[cursor] === ')') {
          depth -= 1;
        }
        cursor += 1;
      }
      if (depth === 0) {
        flushPlain(index);
        segments.push({ text: chars.slice(index, cursor).join(''), protected: true, inlineCode: false });
        plainStart = cursor;
        index = cursor;
        continue;
      }
    }

    if (char === 'h' && matchesUrlPrefix(chars, index)) {
      let cursor = index;
      while (cursor < chars.length && !isUrlBoundary(chars, cursor)) {
        cursor += 1;
      }
      flushPlain(index);
      segments.push({ text: chars.slice(index, cursor).join(''), protected: true, inlineCode: false });
      plainStart = cursor;
      index = cursor;
      continue;
    }

    index += 1;
  }

  flushPlain(chars.length);
  return segments;
}

function firstCharacter(segments: Segment[]): string | undefined {
  for (const segment of segments) {
    if (segment.text.length > 0) {
      return Array.from(segment.text)[0];
    }
  }
  return undefined;
}

function normalizeFullwidthAlphanumerics(chars: string[]): string[] {
  return chars.map((char) => {
    const code = char.codePointAt(0) ?? 0;
    if (
      (code >= 0xff10 && code <= 0xff19) ||
      (code >= 0xff21 && code <= 0xff3a) ||
      (code >= 0xff41 && code <= 0xff5a)
    ) {
      return String.fromCodePoint(code - 0xfee0);
    }
    return char;
  });
}

function normalizeEllipsis(chars: string[]): string[] {
  const result: string[] = [];
  let index = 0;
  while (index < chars.length) {
    if (chars[index] === '.' && isCjk(result[result.length - 1])) {
      let cursor = index;
      while (chars[cursor] === '.') {
        cursor += 1;
      }
      if (cursor - index >= 3) {
        result.push('…', '…');
        index = cursor;
        continue;
      }
    }
    result.push(chars[index]);
    index += 1;
  }
  return result;
}

function lastNonSpace(chars: string[]): string | undefined {
  for (let index = chars.length - 1; index >= 0; index -= 1) {
    if (chars[index] !== ' ') {
      return chars[index];
    }
  }
  return undefined;
}

function firstNonSpace(chars: string[], from: number): string | undefined {
  for (let index = from; index < chars.length; index += 1) {
    if (chars[index] !== ' ') {
      return chars[index];
    }
  }
  return undefined;
}

function normalizeEmDash(chars: string[]): string[] {
  const result: string[] = [];
  let index = 0;
  while (index < chars.length) {
    if (chars[index] === '—') {
      let cursor = index;
      while (chars[cursor] === '—') {
        cursor += 1;
      }
      if (cursor - index === 1) {
        const previous = lastNonSpace(result);
        const next = firstNonSpace(chars, cursor);
        if (isCjk(previous) && isCjk(next)) {
          while (result[result.length - 1] === ' ') {
            result.pop();
          }
          result.push('，');
          index = cursor;
          while (chars[index] === ' ') {
            index += 1;
          }
          continue;
        }
      }
      result.push(...chars.slice(index, cursor));
      index = cursor;
      continue;
    }
    result.push(chars[index]);
    index += 1;
  }
  return result;
}

function normalizePunctuationWidth(chars: string[], leadingContext?: string): string[] {
  const result = [...chars];
  for (let index = 0; index < result.length; index += 1) {
    const fullwidth = PUNCTUATION_MAP[result[index]];
    if (!fullwidth) {
      continue;
    }
    const previous = index > 0 ? result[index - 1] : leadingContext;
    if (!isCjk(previous)) {
      continue;
    }
    const next = index + 1 < result.length ? result[index + 1] : undefined;
    if (next === undefined || next === ' ' || isCjk(next)) {
      result[index] = fullwidth;
    }
  }
  return result;
}

function insertPanguSpacing(chars: string[]): string[] {
  const result: string[] = [];
  for (const char of chars) {
    const previous = result[result.length - 1];
    if ((isCjk(previous) && isLatinOrDigit(char)) || (isLatinOrDigit(previous) && isCjk(char))) {
      result.push(' ');
    }
    result.push(char);
  }
  return result;
}

function trimSpacesAroundFullwidthPunctuation(chars: string[]): string[] {
  const result: string[] = [];
  let index = 0;
  while (index < chars.length) {
    if (chars[index] === ' ') {
      let cursor = index;
      while (chars[cursor] === ' ') {
        cursor += 1;
      }
      if (
        cursor < chars.length &&
        (FULLWIDTH_PUNCTUATION.has(chars[cursor]) || FULLWIDTH_CLOSERS.has(chars[cursor]))
      ) {
        index = cursor;
        continue;
      }
      const previous = result[result.length - 1];
      if (previous && (FULLWIDTH_PUNCTUATION.has(previous) || FULLWIDTH_OPENERS.has(previous))) {
        index = cursor;
        continue;
      }
    }
    result.push(chars[index]);
    index += 1;
  }
  return result;
}

function cleanPlainText(text: string, leadingContext?: string): string {
  let chars = Array.from(text);
  chars = normalizeFullwidthAlphanumerics(chars);
  chars = normalizeEllipsis(chars);
  chars = normalizeEmDash(chars);
  chars = normalizePunctuationWidth(chars, leadingContext);
  chars = insertPanguSpacing(chars);
  chars = trimSpacesAroundFullwidthPunctuation(chars);
  return chars.join('');
}

function cleanLineCore(line: string): string {
  const segments = parseSegments(line);
  let result = '';
  let context: string | undefined;

  segments.forEach((segment, index) => {
    if (segment.protected) {
      if (segment.inlineCode) {
        const last = result[result.length - 1];
        if (isCjk(last) || isLatinOrDigit(last)) {
          result += ' ';
        }
        result += segment.text;
        const next = firstCharacter(segments.slice(index + 1));
        if (isCjk(next) || isLatinOrDigit(next)) {
          result += ' ';
        }
      } else {
        result += segment.text;
        context = segment.text[segment.text.length - 1];
      }
    } else {
      const cleaned = cleanPlainText(segment.text, context);
      result += cleaned;
      if (cleaned.length > 0) {
        context = cleaned[cleaned.length - 1];
      }
    }
  });

  return result;
}

export function cleanLine(line: string): string {
  let trailingSpaces = 0;
  for (let index = line.length - 1; index >= 0 && line[index] === ' '; index -= 1) {
    trailingSpaces += 1;
  }
  const core = trailingSpaces > 0 ? line.slice(0, -trailingSpaces) : line;
  const cleaned = cleanLineCore(core);
  return trailingSpaces >= 2 ? cleaned + ' '.repeat(trailingSpaces) : cleaned;
}

export function cleanTypography(markdown: string): string {
  const lines = markdown.split('\n');
  const output: string[] = [];
  let inFence = false;
  let fenceMarker = '`';
  let fenceLength = 0;
  let inFrontmatter = lines[0] === '---';
  let inBlockMath = false;
  let blankRun = 0;
  let previousWasIndentedCode = false;

  lines.forEach((line, index) => {
    const trimmed = line.trim();
    const quoted = unquoted(trimmed);

    if (inFrontmatter) {
      output.push(line);
      if (index > 0 && (trimmed === '---' || trimmed === '...')) {
        inFrontmatter = false;
      }
      return;
    }

    if (inFence) {
      output.push(line);
      if (isFenceLine(quoted, fenceMarker, fenceLength, true)) {
        inFence = false;
      }
      blankRun = 0;
      return;
    }

    if (inBlockMath) {
      output.push(line);
      if (doubleDollarCount(quoted) % 2 === 1) {
        inBlockMath = false;
      }
      blankRun = 0;
      return;
    }

    if (quoted.startsWith('$$') && doubleDollarCount(quoted) % 2 === 1) {
      inBlockMath = true;
      output.push(line);
      blankRun = 0;
      previousWasIndentedCode = false;
      return;
    }

    const fence = openingFence(quoted);
    if (fence) {
      inFence = true;
      fenceMarker = fence.marker;
      fenceLength = fence.length;
      output.push(line);
      blankRun = 0;
      previousWasIndentedCode = false;
      return;
    }

    if (line.startsWith('    ') || line.startsWith('\t')) {
      output.push(line);
      blankRun = 0;
      previousWasIndentedCode = true;
      return;
    }

    if (trimmed.length === 0) {
      if (previousWasIndentedCode) {
        output.push(line);
      } else {
        blankRun += 1;
        if (blankRun === 1) {
          output.push(line);
        }
      }
      return;
    }

    if (isReferenceDefinition(quoted)) {
      output.push(line);
      blankRun = 0;
      previousWasIndentedCode = false;
      return;
    }

    blankRun = 0;
    previousWasIndentedCode = false;
    output.push(cleanLine(line));
  });

  if (markdown.endsWith('\n') && output[output.length - 1] !== '') {
    output.push('');
  }

  return output.join('\n');
}
