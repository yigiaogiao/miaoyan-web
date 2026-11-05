import {
  autocompletion,
  closeBrackets,
  closeBracketsKeymap,
} from '@codemirror/autocomplete';
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { markdown } from '@codemirror/lang-markdown';
import { bracketMatching, HighlightStyle, syntaxHighlighting } from '@codemirror/language';
import { searchKeymap } from '@codemirror/search';
import { EditorState } from '@codemirror/state';
import {
  drawSelection,
  dropCursor,
  EditorView,
  keymap,
  placeholder as placeholderExt,
  rectangularSelection,
} from '@codemirror/view';
import { tags } from '@lezer/highlight';
import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
  type CSSProperties,
  type Ref,
} from 'react';
import { slashCompletionSource } from '../lib/slash';

export interface CodeEditorHandle {
  insertText: (text: string, position?: number) => void;
  wrapSelection: (before: string, after: string, placeholder?: string) => void;
  toggleHeading: (level: number) => void;
  toggleLinePrefix: (prefix: string, ordered?: boolean) => void;
  scrollToLine: (line: number) => void;
  replaceAll: (value: string) => void;
  focus: () => void;
}

interface Props {
  value: string;
  onChange: (value: string) => void;
  onSave?: () => void;
  onAttachFiles?: (files: File[], position: number | null) => void;
  onVisibleLine?: (line: number) => void;
  placeholder?: string;
  readOnly?: boolean;
  typewriter?: boolean;
  fontSize?: number;
  lineHeight?: number;
  contentWidth?: number;
}

const highlightStyle = HighlightStyle.define([
  { tag: tags.heading1, fontSize: '1.5em', fontWeight: '650', color: 'var(--md-heading)' },
  { tag: tags.heading2, fontSize: '1.3em', fontWeight: '650', color: 'var(--md-heading)' },
  { tag: tags.heading3, fontSize: '1.15em', fontWeight: '650', color: 'var(--md-heading)' },
  { tag: [tags.heading4, tags.heading5, tags.heading6], fontWeight: '650', color: 'var(--md-heading)' },
  { tag: tags.strong, fontWeight: '650', color: 'var(--md-strong)' },
  { tag: tags.emphasis, fontStyle: 'italic' },
  { tag: tags.strikethrough, textDecoration: 'line-through', color: 'var(--text-faint)' },
  { tag: [tags.link, tags.url], color: 'var(--md-link)', textDecoration: 'underline', textUnderlineOffset: '2px' },
  { tag: tags.monospace, fontFamily: 'var(--font-mono)', color: 'var(--md-code)' },
  { tag: tags.quote, color: 'var(--md-quote)', fontStyle: 'italic' },
  { tag: tags.meta, color: 'var(--text-faint)' },
  { tag: tags.comment, color: 'var(--text-faint)', fontStyle: 'italic' },
  { tag: tags.list, color: 'var(--md-list)' },
  { tag: tags.processingInstruction, color: 'var(--text-faint)' },
]);

/** Fewer tags for notes past the editing gate; the parser still works. */
const compactHighlightStyle = HighlightStyle.define([
  { tag: tags.heading1, fontSize: '1.4em', fontWeight: '650', color: 'var(--md-heading)' },
  { tag: [tags.heading2, tags.heading3], fontWeight: '650', color: 'var(--md-heading)' },
  { tag: tags.strong, fontWeight: '650', color: 'var(--md-strong)' },
  { tag: tags.emphasis, fontStyle: 'italic' },
  { tag: tags.monospace, fontFamily: 'var(--font-mono)', color: 'var(--md-code)' },
]);

function countNewlines(value: string): number {
  let count = 0;
  for (let index = 0; index < value.length; index += 1) {
    if (value.charCodeAt(index) === 10) {
      count += 1;
    }
  }
  return count;
}

function selectedLines(view: EditorView): { from: number; to: number }[] {
  const state = view.state;
  const selection = state.selection.main;
  const first = state.doc.lineAt(selection.from);
  const last = state.doc.lineAt(selection.to);
  const lines: { from: number; to: number }[] = [];
  for (let number = first.number; number <= last.number; number += 1) {
    const line = state.doc.line(number);
    lines.push({ from: line.from, to: line.to });
  }
  return lines;
}

const editorTheme = EditorView.theme({
  '&': {
    height: '100%',
    backgroundColor: 'transparent',
    fontSize: 'var(--editor-font-size, 15px)',
  },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': {
    fontFamily: 'var(--font-mono)',
    lineHeight: 'var(--editor-line-height, 1.75)',
    overflow: 'auto',
  },
  '.cm-content': {
    padding: 'var(--editor-padding-y, 2.5rem) var(--editor-padding-x, 2.5rem) 40vh',
    maxWidth: 'var(--editor-width, 860px)',
    margin: '0 auto',
    caretColor: 'var(--accent)',
    width: '100%',
  },
  '.cm-line': { padding: '0' },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--accent)', borderLeftWidth: '2px' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection': {
    backgroundColor: 'var(--selection)',
  },
  '.cm-activeLine': { backgroundColor: 'transparent' },
  '.cm-placeholder': { color: 'var(--text-faint)', fontStyle: 'normal' },
  '.cm-tooltip': {
    backgroundColor: 'var(--surface-raised)',
    border: '1px solid var(--border)',
    borderRadius: '8px',
  },
  '.cm-panels': { backgroundColor: 'var(--surface-raised)' },
});

function CodeEditorInner(
  {
    value,
    onChange,
    onSave,
    onAttachFiles,
    onVisibleLine,
    placeholder,
    readOnly = false,
    typewriter = false,
    fontSize,
    lineHeight,
    contentWidth,
  }: Props,
  ref: Ref<CodeEditorHandle>,
) {
  const hostRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<EditorView | null>(null);
  const callbacks = useRef({ onChange, onSave, onAttachFiles, onVisibleLine, typewriter });
  callbacks.current = { onChange, onSave, onAttachFiles, onVisibleLine, typewriter };

  useImperativeHandle(ref, () => ({
    insertText(text, position) {
      const view = viewRef.current;
      if (!view) {
        return;
      }
      const pos = position ?? view.state.selection.main.head;
      view.dispatch({
        changes: { from: pos, insert: text },
        selection: { anchor: pos + text.length },
        scrollIntoView: true,
      });
      view.focus();
    },
    wrapSelection(before, after, placeholderText) {
      const view = viewRef.current;
      if (!view) {
        return;
      }
      const { from, to } = view.state.selection.main;
      const selected = view.state.sliceDoc(from, to) || placeholderText || '';
      const insert = before + selected + after;
      view.dispatch({
        changes: { from, to, insert },
        selection: {
          anchor: from + before.length,
          head: from + before.length + selected.length,
        },
        scrollIntoView: true,
      });
      view.focus();
    },
    toggleHeading(level) {
      const view = viewRef.current;
      if (!view) {
        return;
      }
      const prefix = `${'#'.repeat(level)} `;
      const changes = selectedLines(view).map((line) => {
        const text = view.state.sliceDoc(line.from, line.to);
        const stripped = text.replace(/^#{1,6}\s+/, '');
        const already = text.startsWith(prefix);
        return {
          from: line.from,
          to: line.from + (text.length - stripped.length),
          insert: already ? '' : prefix,
        };
      });
      view.dispatch({ changes, scrollIntoView: true });
      view.focus();
    },
    toggleLinePrefix(prefix, ordered = false) {
      const view = viewRef.current;
      if (!view) {
        return;
      }
      const lines = selectedLines(view);
      const existing = (line: { from: number; to: number }) => {
        const text = view.state.sliceDoc(line.from, line.to);
        const match = ordered ? /^\d+\.\s+/.exec(text) : text.startsWith(prefix) ? prefix : null;
        return match?.[0] ?? null;
      };
      const allHave = lines.every((line) => existing(line) !== null);
      const changes = lines.map((line, index) => {
        const marker = ordered ? `${index + 1}. ` : prefix;
        const current = existing(line);
        if (allHave && current) {
          return { from: line.from, to: line.from + current.length, insert: '' };
        }
        if (!allHave && current) {
          return {
            from: line.from,
            to: line.from + current.length,
            insert: marker,
          };
        }
        return { from: line.from, insert: marker };
      });
      view.dispatch({ changes, scrollIntoView: true });
      view.focus();
    },
    scrollToLine(line) {
      const view = viewRef.current;
      if (!view) {
        return;
      }
      const number = Math.min(Math.max(1, line + 1), view.state.doc.lines);
      const target = view.state.doc.line(number);
      view.dispatch({
        effects: EditorView.scrollIntoView(target.from, { y: 'start' }),
      });
    },
    replaceAll(next) {
      const view = viewRef.current;
      if (!view) {
        return;
      }
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: next },
      });
      view.focus();
    },
    focus() {
      viewRef.current?.focus();
    },
  }));

  useEffect(() => {
    if (!hostRef.current) {
      return;
    }

    // MiaoYan's large-note gates: past 1 MB drop the Markdown parser and
    // highlighting; past 5000 lines keep a compact highlight and no `/` menu.
    // The instance is created once, so this reflects the size at open time.
    const hugeNote = value.length > 1_000_000;
    const manyLines = !hugeNote && countNewlines(value) > 5000;

    const view = new EditorView({
      parent: hostRef.current,
      state: EditorState.create({
        doc: value,
        extensions: [
          history(),
          drawSelection(),
          dropCursor(),
          rectangularSelection(),
          bracketMatching(),
          closeBrackets(),
          ...(hugeNote
            ? []
            : [
                autocompletion({
                  override: manyLines ? [] : [slashCompletionSource],
                  activateOnTyping: true,
                  icons: false,
                  defaultKeymap: true,
                }),
                markdown(),
                syntaxHighlighting(manyLines ? compactHighlightStyle : highlightStyle),
              ]),
          EditorState.allowMultipleSelections.of(true),
          EditorView.lineWrapping,
          placeholderExt(placeholder ?? '开始写点什么…'),
          EditorState.readOnly.of(readOnly),
          keymap.of([
            {
              key: 'Mod-s',
              run: () => {
                callbacks.current.onSave?.();
                return true;
              },
            },
            ...closeBracketsKeymap,
            ...defaultKeymap,
            ...historyKeymap,
            ...searchKeymap,
            indentWithTab,
          ]),
          editorTheme,
          EditorView.domEventHandlers({
            paste(event) {
              const files = Array.from(event.clipboardData?.files ?? []);
              if (files.length === 0) {
                return false;
              }
              event.preventDefault();
              callbacks.current.onAttachFiles?.(files, null);
              return true;
            },
            drop(event, view) {
              const files = Array.from(event.dataTransfer?.files ?? []);
              if (files.length === 0) {
                return false;
              }
              event.preventDefault();
              const position = view.posAtCoords({ x: event.clientX, y: event.clientY });
              callbacks.current.onAttachFiles?.(files, position);
              return true;
            },
          }),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) {
              callbacks.current.onChange(update.state.doc.toString());
            }

            if (
              callbacks.current.typewriter &&
              (update.docChanged || update.selectionSet)
            ) {
              const head = update.state.selection.main.head;
              window.requestAnimationFrame(() => {
                const current = viewRef.current;
                if (!current || !callbacks.current.typewriter) {
                  return;
                }
                const coords = current.coordsAtPos(head);
                const box = current.scrollDOM.getBoundingClientRect();
                if (
                  coords &&
                  (coords.top < box.top + box.height * 0.3 ||
                    coords.bottom > box.bottom - box.height * 0.3)
                ) {
                  current.dispatch({
                    effects: EditorView.scrollIntoView(head, { y: 'center' }),
                  });
                }
              });
            }
          }),
        ],
      }),
    });

    viewRef.current = view;

    let lastLine = -1;
    let frame = 0;
    const scroller = view.scrollDOM;
    const onScroll = () => {
      if (frame) {
        return;
      }
      frame = window.requestAnimationFrame(() => {
        frame = 0;
        const block = view.lineBlockAtHeight(scroller.scrollTop + 12);
        const line = view.state.doc.lineAt(block.from).number - 1;
        if (line !== lastLine) {
          lastLine = line;
          callbacks.current.onVisibleLine?.(line);
        }
      });
    };
    scroller.addEventListener('scroll', onScroll, { passive: true });

    return () => {
      scroller.removeEventListener('scroll', onScroll);
      if (frame) {
        window.cancelAnimationFrame(frame);
      }
      view.destroy();
      viewRef.current = null;
    };
    // The editor instance is intentionally created once; value sync is handled below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    const view = viewRef.current;
    if (!view) {
      return;
    }
    if (view.state.doc.toString() === value) {
      return;
    }
    view.dispatch({
      changes: { from: 0, to: view.state.doc.length, insert: value },
    });
  }, [value]);

  return (
    <div
      ref={hostRef}
      className="h-full overflow-hidden"
      data-testid="code-editor"
      style={
        {
          '--editor-font-size': `${fontSize ?? 15}px`,
          '--editor-line-height': String(lineHeight ?? 1.75),
          '--editor-width': `${contentWidth ?? 860}px`,
        } as CSSProperties
      }
    />
  );
}

export const CodeEditor = forwardRef(CodeEditorInner);
