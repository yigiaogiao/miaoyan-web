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
      }),
    },
  ),
);
