import type { CompletionContext, CompletionResult } from '@codemirror/autocomplete';
import type { EditorView } from '@codemirror/view';
import { t } from './i18n';

interface SlashTemplate {
  key: string;
  label: string;
  detail: string;
  insert: string;
  /** Cursor offset from the start of `insert`. Defaults to the end. */
  cursor?: number;
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

function currentTimestamp(): string {
  const now = new Date();
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
}

function templates(): SlashTemplate[] {
  const today = new Date().toISOString().slice(0, 10);
  const image = '<img src="" width="800">';
  const video =
    '<video width="800px" preload="metadata" loop autoplay controls muted><source src="" type="video/mp4"></video>';
  return [
    { key: 'h1', label: t('slash.h1'), detail: '# ', insert: '# ' },
    { key: 'h2', label: t('slash.h2'), detail: '## ', insert: '## ' },
    { key: 'h3', label: t('slash.h3'), detail: '### ', insert: '### ' },
    { key: 'ul', label: t('slash.ul'), detail: '- ', insert: '- ' },
    { key: 'ol', label: t('slash.ol'), detail: '1. ', insert: '1. ' },
    { key: 'todo', label: t('slash.todo'), detail: '- [ ] ', insert: '- [ ] ' },
    { key: 'quote', label: t('slash.quote'), detail: '> ', insert: '> ' },
    {
      key: 'code',
      label: t('slash.code'),
      detail: '```language',
      insert: '```\n\n```\n',
      cursor: 4,
    },
    {
      key: 'math',
      label: t('slash.math'),
      detail: '$$ … $$',
      insert: '$$\n\n$$\n',
      cursor: 3,
    },
    {
      key: 'table',
      label: t('slash.table'),
      detail: t('slash.tableDetail'),
      insert: t('slash.tableContent'),
    },
    {
      key: 'mermaid',
      label: t('slash.mermaid'),
      detail: '```mermaid',
      insert: t('slash.mermaidContent'),
    },
    {
      key: 'markmap',
      label: t('slash.markmap'),
      detail: '```markmap',
      insert: t('slash.markmapContent'),
    },
    {
      key: 'alert',
      label: t('slash.alert'),
      detail: '> [!NOTE]',
      insert: '> [!NOTE]\n> ',
    },
    {
      key: 'ppt',
      label: t('slash.ppt'),
      detail: t('slash.pptDetail'),
      insert: t('slash.pptContent'),
    },
    {
      key: 'plantuml',
      label: t('slash.plantuml'),
      detail: '```plantuml',
      insert: t('slash.plantumlContent'),
    },
    {
      key: 'fold',
      label: t('slash.fold'),
      detail: t('slash.foldDetail'),
      insert: t('slash.foldContent'),
    },
    {
      key: 'task',
      label: t('slash.task'),
      detail: t('slash.taskDetail'),
      insert: t('slash.taskContent'),
    },
    { key: 'divider', label: t('slash.divider'), detail: '---', insert: '---\n' },
    { key: 'date', label: t('slash.date'), detail: today, insert: today },
    {
      key: 'time',
      label: t('slash.time'),
      detail: 'yyyy-MM-dd HH:mm:ss',
      insert: currentTimestamp(),
    },
    {
      key: 'img',
      label: t('slash.img'),
      detail: t('slash.imgDetail'),
      insert: image,
      cursor: image.indexOf('src=""') + 5,
    },
    {
      key: 'video',
      label: t('slash.video'),
      detail: t('slash.videoDetail'),
      insert: video,
      cursor: video.indexOf('src=""') + 5,
    },
    {
      key: 'link',
      label: t('slash.link'),
      detail: '[[…]]',
      insert: '[[]]',
      cursor: 2,
    },
  ];
}

/**
 * Slash menu at the start of the current line: typing `/` lists the templates
 * MiaoYan users reach for most often.
 */
export function slashCompletionSource(context: CompletionContext): CompletionResult | null {
  const line = context.state.doc.lineAt(context.pos);
  const before = context.state.sliceDoc(line.from, context.pos);
  const match = /^\s*\/([\p{L}\w]*)$/u.exec(before);
  if (!match) {
    return null;
  }

  const from = line.from + before.lastIndexOf('/');
  return {
    from,
    validFor: /^\/[\p{L}\w]*$/u,
    options: templates().map((template) => ({
      label: `/${template.key}`,
      detail: template.label,
      info: template.detail,
      apply(view: EditorView) {
        const cursor = from + (template.cursor ?? template.insert.length);
        view.dispatch({
          changes: { from, to: context.pos, insert: template.insert },
          selection: { anchor: cursor },
        });
      },
    })),
  };
}
