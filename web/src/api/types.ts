export type ViewMode = 'editor' | 'split' | 'preview';
export type ThemeMode = 'system' | 'light' | 'dark';
export type NoteSort = 'modified' | 'created' | 'title';

export interface Project {
  id: string;
  name: string;
  path: string;
  noteCount: number;
  kind: 'root' | 'folder';
}

export interface NoteSummary {
  id: string;
  title: string;
  path: string;
  projectId: string;
  projectName: string;
  folder: string;
  excerpt: string;
  modifiedAt: string;
  createdAt: string;
  size: number;
  isUntitled: boolean;
  pinned: boolean;
}

export interface Note extends NoteSummary {
  content: string;
  etag: string;
  hasBom: boolean;
}

export interface SaveNoteBody {
  content: string;
  etag: string | null;
}

export interface ConflictInfo {
  error: 'conflict';
  message: string;
  currentEtag: string;
  conflictPath: string;
  currentContent: string;
}

export interface TrashEntry {
  id: string;
  kind: 'note' | 'project';
  title: string;
  originalPath: string;
  deletedAt: string;
  size: number;
}

export interface TrashRestoreResult {
  kind: 'note' | 'project';
  noteId: string | null;
  projectId: string | null;
}

export interface Asset {
  url: string;
  markdown: string;
  name: string;
  size: number;
  kind: string;
}

export interface MediaToolResult {
  kind: 'ocr' | 'transcribe';
  text: string;
}

export interface UnusedAssets {
  scannedNotes: number;
  files: string[];
}

export interface CleanUnusedAssetsResult {
  removed: string[];
  failed: string[];
}

export interface LinkTarget {
  title: string;
  noteId: string;
  projectName: string;
  path: string;
}

export interface UnresolvedLink {
  title: string;
  count: number;
}

export interface LinkIndex {
  targets: LinkTarget[];
  unresolved: UnresolvedLink[];
}

export interface Backlink {
  noteId: string;
  title: string;
  projectName: string;
  line: number;
  excerpt: string;
}

export interface SearchHit {
  id: string;
  title: string;
  projectId: string;
  projectName: string;
  snippet: string;
  modifiedAt: string;
  size: number;
  isUntitled: boolean;
}

export interface NoteVersion {
  id: string;
  createdAt: string;
  size: number;
  etag: string;
  isCurrent: boolean;
}

export interface NoteVersionDetail {
  id: string;
  content: string;
  createdAt: string;
  size: number;
  etag: string;
}

export interface VaultInfo {
  name: string;
  root: string;
  noteCount: number;
  projectCount: number;
}

export interface ChangeEvent {
  kind: string;
  noteId: string | null;
  path: string | null;
  at: string;
}

export interface AuthStatus {
  required: boolean;
  authenticated: boolean;
  password: boolean;
  apiKey: boolean;
}
