import { closeBrackets, closeBracketsKeymap } from '@codemirror/autocomplete';
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
  type Ref,
} from 'react';

export interface CodeEditorHandle {
  insertText: (text: string, position?: number) => void;
  focus: () => void;
}

interface Props {
  value: string;
  onChange: (value: string) => void;
  onSave?: () => void;
  onAttachFiles?: (files: File[], position: number | null) => void;
  placeholder?: string;
  readOnly?: boolean;
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

const editorTheme = EditorView.theme({
  '&': {
    height: '100%',
    backgroundColor: 'transparent',
    fontSize: '15px',
  },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': {
    fontFamily: 'var(--font-mono)',
    lineHeight: '1.75',
    overflow: 'auto',
  },
  '.cm-content': {
    padding: 'var(--editor-padding-y, 2.5rem) var(--editor-padding-x, 2.5rem) 40vh',
    maxWidth: '860px',
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
  { value, onChange, onSave, onAttachFiles, placeholder, readOnly = false }: Props,
  ref: Ref<CodeEditorHandle>,
) {
  const hostRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<EditorView | null>(null);
  const callbacks = useRef({ onChange, onSave, onAttachFiles });
  callbacks.current = { onChange, onSave, onAttachFiles };

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
    focus() {
      viewRef.current?.focus();
    },
  }));

  useEffect(() => {
    if (!hostRef.current) {
      return;
    }

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
          EditorState.allowMultipleSelections.of(true),
          EditorView.lineWrapping,
          markdown(),
          syntaxHighlighting(highlightStyle),
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
          }),
        ],
      }),
    });

    viewRef.current = view;
    return () => {
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

  return <div ref={hostRef} className="h-full overflow-hidden" data-testid="code-editor" />;
}

export const CodeEditor = forwardRef(CodeEditorInner);
