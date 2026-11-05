import { renderDiagrams } from './diagrams';
import { currentLanguage, t } from './i18n';
import { renderMarkdown } from './markdown';

const EXPORT_CSS = `
:root{color-scheme:light;--text:#26251f;--text-muted:#65635a;--text-faint:#8b887d;--accent:#2f7d63;--accent-soft:#e4f0ea;--border:#e5e1d8;--danger:#a23b32;--danger-soft:#f7e6e3;--warning:#8a6508;--warning-soft:#fbf2da;--code-bg:#f1efe8;--rule:#e5e1d8;--table-head:#f1efe8;--md-heading:#1e1d18;--md-code:#8b4a2f;}
*{box-sizing:border-box}
body{margin:0;background:#f6f5f1;color:var(--text);font-family:'Inter','PingFang SC','Microsoft YaHei',system-ui,sans-serif}
article.md-preview{max-width:820px;margin:40px auto;padding:48px 56px;background:#fffefa;border:1px solid var(--border);border-radius:12px;font-size:15px;line-height:1.78;overflow-wrap:anywhere}
.md-preview h1,.md-preview h2,.md-preview h3,.md-preview h4,.md-preview h5,.md-preview h6{margin:1.6em 0 .55em;font-weight:650;line-height:1.3;color:var(--md-heading)}
.md-preview h1{font-size:1.7em;padding-bottom:.3em;border-bottom:1px solid var(--rule)}
.md-preview h2{font-size:1.4em}.md-preview h3{font-size:1.18em}
.md-preview p,.md-preview ul,.md-preview ol,.md-preview blockquote,.md-preview pre,.md-preview table{margin:.85em 0}
.md-preview a{color:var(--accent);text-decoration:underline;text-underline-offset:2px}
.md-preview .wikilink{padding:0 .1em;border-radius:3px;background:var(--accent-soft);color:var(--accent);text-decoration:none}
.md-preview .wikilink-unresolved{background:transparent;color:var(--text-faint);text-decoration:underline dashed}
.md-preview ul,.md-preview ol{padding-left:1.4em}.md-preview ul{list-style:disc}.md-preview ol{list-style:decimal}
.md-preview li{margin:.28em 0}.md-preview li::marker{color:var(--text-faint)}
.md-preview blockquote{margin:1em 0;padding:.15em 1em;border-left:3px solid #b7d7c8;color:var(--text-muted)}
.md-preview code{padding:.15em .38em;border-radius:5px;background:var(--code-bg);font-family:'JetBrains Mono',ui-monospace,monospace;font-size:.88em;color:var(--md-code)}
.md-preview pre{padding:1em 1.15em;border:1px solid var(--border);border-radius:10px;background:var(--code-bg);overflow-x:auto;line-height:1.6}
.md-preview pre code{padding:0;background:transparent;font-size:.86em;color:var(--text)}
.md-preview table{width:100%;border-collapse:collapse;font-size:.94em}
.md-preview th,.md-preview td{padding:.5em .75em;border:1px solid var(--border);text-align:left}
.md-preview th{background:var(--table-head)}
.md-preview hr{height:1px;margin:2em 0;border:0;background:var(--rule)}
.md-preview img{max-width:100%;height:auto;border-radius:8px}
.md-preview .math-block{margin:1em 0;text-align:center;overflow-x:auto}
.md-preview .diagram{margin:1.1em 0;padding:.9em;border:1px solid var(--border);border-radius:10px;background:#fffefe;overflow-x:auto}
.md-preview .diagram-source{display:none}
.md-preview .diagram-canvas{display:flex;justify-content:center}
.md-preview .diagram-canvas svg{max-width:100%;height:auto}
.md-preview .markmap-svg{width:100%;height:360px}
.md-preview blockquote.alert{margin:1em 0;padding:.85em 1em;border:1px solid #b7d7c8;border-left-width:3px;border-radius:8px;background:var(--accent-soft)}
.md-preview blockquote.alert::before{content:attr(data-alert-label);display:block;margin-bottom:.35em;font-size:.82em;font-weight:650;color:var(--accent)}
.md-preview blockquote.alert-warning{border-color:var(--warning);background:var(--warning-soft)}
.md-preview blockquote.alert-caution{border-color:var(--danger);background:var(--danger-soft)}
.md-preview .footnotes{margin-top:2.4em;padding-top:1em;border-top:1px solid var(--rule);font-size:.9em;color:var(--text-muted)}
.md-preview .footnotes-sep{display:none}
.md-preview .task-list-item{list-style:none;margin-left:-1.2em}
.md-preview .hljs-comment,.md-preview .hljs-quote{color:var(--text-faint);font-style:italic}
.md-preview .hljs-keyword,.md-preview .hljs-selector-tag,.md-preview .hljs-literal{color:var(--md-code)}
.md-preview .hljs-string,.md-preview .hljs-attr{color:var(--accent)}
.md-preview .hljs-number,.md-preview .hljs-built_in{color:var(--warning)}
.md-preview .hljs-title,.md-preview .hljs-function{color:var(--md-heading);font-weight:600}
@media print{body{background:#fff}article.md-preview{max-width:none;margin:0;padding:0;border:0}pre{white-space:pre-wrap}}
`;

function safeFilename(title: string): string {
  return (title.trim() || t('common.unnamedNote')).replace(/[\\/:*?"<>|\n]/g, '-').slice(0, 120);
}

export function downloadText(filename: string, text: string, mime: string): void {
  const blob = new Blob([text], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  // Keep the anchor until the browser has picked up the download name.
  window.setTimeout(() => {
    anchor.remove();
    URL.revokeObjectURL(url);
  }, 2000);
}

export function exportMarkdown(title: string, content: string): void {
  downloadText(`${safeFilename(title)}.md`, content, 'text/markdown;charset=utf-8');
}

/** Copies the KaTeX rules (including font URLs) out of the running app. */
async function collectKatexCss(): Promise<string> {
  const chunks: string[] = [];
  for (const sheet of Array.from(document.styleSheets)) {
    let rules: CSSRuleList;
    try {
      rules = sheet.cssRules;
    } catch {
      continue;
    }
    for (const rule of Array.from(rules)) {
      const text = rule.cssText;
      if (!/\.katex|KaTeX_/.test(text)) {
        continue;
      }
      chunks.push(
        text.replace(/url\((['"]?)([^)'"]+)\1\)/g, (full, quote: string, url: string) => {
          if (/^(data:|https?:)/.test(url) || !sheet.href) {
            return full;
          }
          try {
            return `url(${quote}${new URL(url, sheet.href).href}${quote})`;
          } catch {
            return full;
          }
        }),
      );
    }
  }
  return chunks.join('\n');
}

async function inlineImages(root: HTMLElement): Promise<void> {
  const images = Array.from(root.querySelectorAll('img'));
  await Promise.all(
    images.map(async (image) => {
      const source = image.getAttribute('src');
      if (!source || /^data:/.test(source)) {
        return;
      }
      try {
        const response = await fetch(source);
        if (!response.ok) {
          return;
        }
        const blob = await response.blob();
        const dataUrl = await new Promise<string>((resolve, reject) => {
          const reader = new FileReader();
          reader.onload = () => resolve(String(reader.result));
          reader.onerror = () => reject(reader.error);
          reader.readAsDataURL(blob);
        });
        image.setAttribute('src', dataUrl);
      } catch {
        // Leave the original URL when the asset cannot be fetched.
      }
    }),
  );
}

export async function buildStandaloneHtml(options: {
  title: string;
  content: string;
  noteId: string;
  linkTitles?: ReadonlySet<string>;
}): Promise<string> {
  const { title, content, noteId, linkTitles } = options;
  const html = renderMarkdown(content, noteId, linkTitles);

  const host = document.createElement('div');
  host.style.cssText =
    'position:fixed;left:-10000px;top:0;width:820px;background:#fff;visibility:hidden';
  host.innerHTML = html;
  document.body.appendChild(host);

  try {
    const diagrams = renderDiagrams(host, 'light');
    await Promise.race([diagrams.done, new Promise((resolve) => setTimeout(resolve, 8000))]);
    diagrams.cancel();
    await inlineImages(host);

    const katexCss = await collectKatexCss();
    const safeTitle = title.replace(/[<>&"]/g, (character) => {
      const entities: Record<string, string> = {
        '<': '&lt;',
        '>': '&gt;',
        '&': '&amp;',
        '"': '&quot;',
      };
      return entities[character];
    });

    return `<!doctype html>
<html lang="${currentLanguage() === 'zh' ? 'zh-CN' : 'en'}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${safeTitle}</title>
<style>${EXPORT_CSS}</style>
<style>${katexCss}</style>
</head>
<body>
<article class="md-preview">${host.innerHTML}</article>
</body>
</html>`;
  } finally {
    host.remove();
  }
}

export async function exportHtml(options: {
  title: string;
  content: string;
  noteId: string;
  linkTitles?: ReadonlySet<string>;
}): Promise<void> {
  const documentHtml = await buildStandaloneHtml(options);
  downloadText(
    `${safeFilename(options.title)}.html`,
    documentHtml,
    'text/html;charset=utf-8',
  );
}

/**
 * PDF export follows the browser's print pipeline: open a window during the
 * click gesture, fill it once assets are ready, then print.
 */
export async function exportPdf(options: {
  title: string;
  content: string;
  noteId: string;
  linkTitles?: ReadonlySet<string>;
  target: Window | null;
}): Promise<void> {
  const { target, ...rest } = options;
  const documentHtml = await buildStandaloneHtml(rest);
  if (!target) {
    downloadText(`${safeFilename(rest.title)}.html`, documentHtml, 'text/html;charset=utf-8');
    return;
  }
  target.document.open();
  target.document.write(documentHtml);
  target.document.close();
  await new Promise((resolve) => setTimeout(resolve, 600));
  target.focus();
  target.print();
}
