import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { NoteSort, ThemeMode, ViewMode } from '../api/types';

export type Language = 'zh' | 'en';

interface UiState {
  selectedProjectId: string;
  viewMode: ViewMode;
  theme: ThemeMode;
  language: Language;
  sort: NoteSort;
  sidebarOpen: boolean;
  listOpen: boolean;
  sidebarWidth: number;
  listWidth: number;
  focusMode: boolean;
  typewriter: boolean;
  editorFontSize: number;
  editorLineHeight: number;
  editorWidth: number;
  projectCreateRequest: number;
  setProject: (id: string) => void;
  setViewMode: (mode: ViewMode) => void;
  setTheme: (theme: ThemeMode) => void;
  setLanguage: (language: Language) => void;
  setSort: (sort: NoteSort) => void;
  toggleSidebar: () => void;
  toggleList: () => void;
  showBoth: () => void;
  setPanels: (sidebarOpen: boolean, listOpen: boolean) => void;
  setSidebarWidth: (width: number) => void;
  setListWidth: (width: number) => void;
  setFocusMode: (enabled: boolean) => void;
  setTypewriter: (enabled: boolean) => void;
  setEditorFontSize: (size: number) => void;
  setEditorLineHeight: (height: number) => void;
  setEditorWidth: (width: number) => void;
  requestCreateProject: () => void;
}

/** The subset written to localStorage. */
type PersistedUiState = Pick<
  UiState,
  | 'viewMode'
  | 'theme'
  | 'language'
  | 'sort'
  | 'sidebarOpen'
  | 'listOpen'
  | 'sidebarWidth'
  | 'listWidth'
  | 'focusMode'
  | 'typewriter'
  | 'editorFontSize'
  | 'editorLineHeight'
  | 'editorWidth'
>;

export const useUiStore = create<UiState>()(
  persist(
    (set) => ({
      selectedProjectId: 'all',
      viewMode: 'split',
      theme: 'system',
      language: 'zh',
      sort: 'modified',
      sidebarOpen: true,
      listOpen: true,
      sidebarWidth: 232,
      listWidth: 326,
      focusMode: false,
      typewriter: false,
      editorFontSize: 17,
      editorLineHeight: 1.7,
      editorWidth: 720,
      projectCreateRequest: 0,
      setProject: (id) => set({ selectedProjectId: id }),
      setViewMode: (viewMode) => set({ viewMode }),
      setTheme: (theme) => set({ theme }),
      setLanguage: (language) => set({ language }),
      setSort: (sort) => set({ sort }),
      toggleSidebar: () => set((state) => ({ sidebarOpen: !state.sidebarOpen })),
      toggleList: () => set((state) => ({ listOpen: !state.listOpen })),
      showBoth: () => set({ sidebarOpen: true, listOpen: true }),
      setPanels: (sidebarOpen, listOpen) => set({ sidebarOpen, listOpen }),
      setSidebarWidth: (sidebarWidth) => set({ sidebarWidth }),
      setListWidth: (listWidth) => set({ listWidth }),
      setFocusMode: (focusMode) => set({ focusMode }),
      setTypewriter: (typewriter) => set({ typewriter }),
      setEditorFontSize: (editorFontSize) => set({ editorFontSize }),
      setEditorLineHeight: (editorLineHeight) => set({ editorLineHeight }),
      setEditorWidth: (editorWidth) => set({ editorWidth }),
      requestCreateProject: () =>
        set((state) => ({ projectCreateRequest: state.projectCreateRequest + 1 })),
    }),
    {
      name: 'miaoyan-ui',
      // 0.1.6 moved the editor from a monospace code look to a reading
      // measure. Only rewrite the sliders that still hold the old defaults;
      // anything the reader tuned by hand stays put.
      version: 3,
      migrate: (persisted) => {
        const state = persisted as PersistedUiState;
        const next = { ...state };
        if (
          state.editorFontSize === 15 &&
          state.editorLineHeight === 1.75 &&
          state.editorWidth === 860
        ) {
          next.editorFontSize = 16;
          next.editorLineHeight = 1.7;
          next.editorWidth = 720;
        }
        // The WenKai reading face sits a touch smaller than the old Songti
        // stack, so the untouched default moves from 16px to 17px.
        if (
          state.editorFontSize === 16 &&
          state.editorLineHeight === 1.7 &&
          state.editorWidth === 720
        ) {
          next.editorFontSize = 17;
        }
        if (state.sidebarWidth === 218) {
          next.sidebarWidth = 232;
        }
        if (state.listWidth === 294) {
          next.listWidth = 326;
        }
        return next;
      },
      partialize: (state) => ({
        viewMode: state.viewMode,
        theme: state.theme,
        language: state.language,
        sort: state.sort,
        sidebarOpen: state.sidebarOpen,
        listOpen: state.listOpen,
        sidebarWidth: state.sidebarWidth,
        listWidth: state.listWidth,
        focusMode: state.focusMode,
        typewriter: state.typewriter,
        editorFontSize: state.editorFontSize,
        editorLineHeight: state.editorLineHeight,
        editorWidth: state.editorWidth,
      }),
    },
  ),
);
