import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { NoteSort, ThemeMode, ViewMode } from '../api/types';

interface UiState {
  selectedProjectId: string;
  viewMode: ViewMode;
  theme: ThemeMode;
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

export const useUiStore = create<UiState>()(
  persist(
    (set) => ({
      selectedProjectId: 'all',
      viewMode: 'split',
      theme: 'system',
      sort: 'modified',
      sidebarOpen: true,
      listOpen: true,
      sidebarWidth: 218,
      listWidth: 294,
      focusMode: false,
      typewriter: false,
      editorFontSize: 15,
      editorLineHeight: 1.75,
      editorWidth: 860,
      projectCreateRequest: 0,
      setProject: (id) => set({ selectedProjectId: id }),
      setViewMode: (viewMode) => set({ viewMode }),
      setTheme: (theme) => set({ theme }),
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
      partialize: (state) => ({
        viewMode: state.viewMode,
        theme: state.theme,
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
