import type { CompletionContext, CompletionResult } from '@codemirror/autocomplete';
import type { EditorView } from '@codemirror/view';

interface SlashTemplate {
  key: string;
  label: string;
  detail: string;
  insert: string;
  /** Cursor offset from the start of `insert`. Defaults to the end. */
  cursor?: number;
}

const today = new Date().toISOString().slice(0, 10);

export const SLASH_TEMPLATES: SlashTemplate[] = [
  { key: 'h1', label: '标题 1', detail: '# 标题', insert: '# ' },
  { key: 'h2', label: '标题 2', detail: '## 标题', insert: '## ' },
  { key: 'h3', label: '标题 3', detail: '### 标题', insert: '### ' },
  { key: 'ul', label: '无序列表', detail: '- 列表项', insert: '- ' },
  { key: 'ol', label: '有序列表', detail: '1. 列表项', insert: '1. ' },
  { key: 'todo', label: '待办事项', detail: '- [ ] 任务', insert: '- [ ] ' },
  { key: 'quote', label: '引用', detail: '> 引用', insert: '> ' },
  {
    key: 'code',
    label: '代码块',
    detail: '```language',
    insert: '```\n\n```\n',
    cursor: 4,
  },
  {
    key: 'math',
    label: '公式块',
    detail: '$$ … $$',
    insert: '$$\n\n$$\n',
    cursor: 3,
  },
  {
    key: 'table',
    label: '表格',
    detail: '3 × 2 表格',
    insert: '| 列 1 | 列 2 | 列 3 |\n| --- | --- | --- |\n| 内容 | 内容 | 内容 |\n',
  },
  {
    key: 'mermaid',
    label: 'Mermaid 图表',
    detail: '```mermaid',
    insert: '```mermaid\ngraph LR\n  A[开始] --> B[结束]\n```\n',
  },
  {
    key: 'markmap',
    label: '思维导图',
    detail: '```markmap',
    insert: '```markmap\n# 中心主题\n## 分支一\n## 分支二\n```\n',
  },
  {
    key: 'alert',
    label: '提示框',
    detail: '> [!NOTE]',
    insert: '> [!NOTE]\n> ',
  },
  {
    key: 'ppt',
    label: 'PPT 分页',
    detail: '--- 新的一页',
    insert: '---\n\n## 新的一页\n\n',
  },
  { key: 'divider', label: '分隔线', detail: '---', insert: '---\n' },
  { key: 'date', label: '今天日期', detail: today, insert: today },
  {
    key: 'link',
    label: '双链笔记',
    detail: '[[笔记标题]]',
    insert: '[[]]',
    cursor: 2,
  },
];

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
    options: SLASH_TEMPLATES.map((template) => ({
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
