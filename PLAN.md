# MiaoYan Web — Implementation Plan

> Status: M0 planning document. This file is the contract for the project; update it
> when a decision changes, and keep `COMPATIBILITY.md` in sync with user-visible
> behaviour.

## 1. Goal

Build **MiaoYan Web**, a self-hosted Markdown knowledge base for LazyCat (懒猫微服)
that reproduces the *function, information architecture and interaction flow* of
[MiaoYan](https://github.com/tw93/MiaoYan) as closely as a browser allows. The
filesystem is the source of truth; the app is a window onto a folder of `.md`
files, not a database with an export feature.

**1:1 definition.** Feature parity, three-column layout, and the same task flow.
Where a native macOS behaviour cannot exist in a browser, the user must still be
able to complete the same task; every such substitution is recorded in
`COMPATIBILITY.md` with the reason and the Web alternative.

This is an independent project. It does **not** modify MiaoYan or Papyra, and it
does not copy Papyra source code. Architecture and API shapes are studied as
references; the implementation is new. License: **MIT** (see `LICENSE`).

### In scope (v1)

- Three-column library: projects/folders, note list, editor + live preview.
- Source editing with Markdown highlighting and split / editor-only / preview-only modes.
- Note and project create / rename / move / delete (trash) / restore.
- Autosave, external-change watching, conflict backups.
- Wikilinks `[[标题]]`, backlinks, unresolved-link affordance.
- Math (KaTeX), Mermaid, PlantUML (local), Markmap.
- PPT mode (reveal.js, `---` separators).
- Light / dark / follow-system themes, focus mode, typewriter mode.
- Full-text search; note-list excerpt and time; pin; sort.
- Version history, diff view, restore.
- `i/` images and `files/` attachments; image paste/drag; audio playback.
- Import an existing MiaoYan library, export a note / deck / whole vault.
- Media ingest for images, text, audio; video keeps audio + transcript only.
- Papyra-compatible API subset so the existing `papyra-ingest` works unchanged.
- LazyCat single-container deployment (`.lpk`), amd64 + arm64.

### Explicitly out of scope (v1)

Realtime collaboration, comments, sharing links, multi-tenancy, native macOS
features (global hotkey, Finder reveal, iCloud coordination), crawlers, full
Papyra auth (passkey / 2FA / registration). v1 is **single user, single
library**, with LazyCat gateway isolation or an optional password.

## 2. Architecture

```
┌───────────────────────────── one container ─────────────────────────────┐
│  Kestrel (:5080)                                                        │
│  ├─ /                 SPA (React build in wwwroot, history fallback)     │
│  ├─ /api/*            Web API + Papyra-compatible API                    │
│  ├─ /api/events       Server-Sent Events (filesystem/index changes)      │
│  ├─ /api/media/*      API media library (Papyra-compatible)              │
│  ├─ /api/notes/*/assets/*  note-relative i/ and files/ serving           │
│  ├─ /note/{id}        SPA note route (compatibility)                    │
│  └─ /healthz          health check                                       │
│                                                                         │
│  VaultService ── filesystem (source of truth)                            │
│  VaultWatcher ── FileSystemWatcher + debounce + write suppression        │
│  IndexService ── SQLite (FTS5) index, always rebuildable                │
│  VersionService ─ state/versions/<note-id>/<timestamp>.md + meta.json    │
│  MediaService ── upload, hash dedupe, safe serving, ffmpeg/whisper hooks  │
│  PlantUmlService ── local `plantuml.jar` over stdin; never public cloud  │
└─────────────────────────────────────────────────────────────────────────┘
        │                                          │
  MIAOYAN_LIBRARY                            MIAOYAN_STATE_DIR
  (documents, persisted)                     (index/cache/versions)
```

- **One process, two storage roots per user.** A `VaultRegistry` resolves a
  runtime from the signed `X-HC-User-ID` header, so one container can serve
  several LazyCat users without mixing notes. The vault is the user's folder
  and is never deleted on upgrade. State (SQLite index, versions, caches) lives
  outside the vault in `/lzcapp/var/miaoyan/<uid-or-hash>`; losing state only
  costs a rebuild.
- **No Redis, no external database, no object store.** SQLite + files only.
- **Frontend** is a static SPA served by the same Kestrel instance; API and UI
  share an origin, so no CORS surface.

### Runtime paths and discovery

| Setting | Env var | Default / discovery |
|---|---|---|
| Vault root | `MIAOYAN_LIBRARY` | `/lzcapp/documents/<uid>/MiaoYan` from `X-HC-User-ID`; explicit value pins one library |
| State root | `MIAOYAN_STATE_DIR` | `/lzcapp/var/miaoyan/<uid-or-hash>` per user, else `./data/.miaoyan-state` |
| Default UID | `MIAOYAN_DEFAULT_UID` | unset; only for API-key calls without a header on multi-user devices |
| Web password | `MIAOYAN_WEB_PASSWORD` | unset = trust LazyCat gateway / LAN |
| API key | `MIAOYAN_API_KEY` | unset = no machine-key requirement |
| Max upload | `MIAOYAN_MAX_UPLOAD_MB` | 100 |
| PlantUML jar | `MIAOYAN_PLANTUML_JAR` | `/opt/plantuml/plantuml.jar` if present |
| Whisper command | `MIAOYAN_WHISPER_CMD` | unset (disabled), e.g. `whisper --model small --output txt {input}` |
| OCR command | `MIAOYAN_OCR_CMD` | unset (disabled), e.g. `tesseract {input} stdout -l chi_sim+eng` |

The `/lzcapp/pkg` mount marks a real app container; if it exists without
`/lzcapp/documents`, startup fails loudly instead of writing into the ephemeral
rootfs. Per-request vaults are created on first use, logged, and probed for
writability. M0 ships the resolver and health output; the on-device path is
confirmed in M2 acceptance.

## 3. Filesystem model

```
MIAOYAN_LIBRARY/
  Project A/                  # every top-level folder is a project
    note.md                   # title = file name without .md
    i/photo.png               # images used by notes in this folder
    files/audio.m4a           # other attachments
  Project B/
    subfolder/                # nested folders are allowed (MiaoYan project subtrees)
      note.md
  Inbox/
  root-note.md                # notes at the root are shown under "All notes"
  i/                          # optional root-level media (API uploads live here)
  files/
  .miaoyan/
    trash/                    # timestamped deleted notes + meta.json
    conflicts/ -> see below
  .miaoyan-conflicts/         # MiaoYan-compatible conflict backups
```

Rules:

- Every direct child directory of the root is a project. Root-level `.md`
  files are valid notes and appear in "All notes".
- `i/`, `files/`, dot-directories (`.miaoyan`, `.git`, `.obsidian`, ...), and
  `node_modules` are never scanned as projects/notes.
- Attachment references are root-relative to the note's folder: `/i/x.png`,
  `/files/y.m4a`. No absolute host paths, ever.
- **Title = file name minus `.md`.** Renaming the title renames the file. A
  collision gets `" 2"`, `" 3"` (MiaoYan's `NameHelper` convention: `Name 2.md`).
  File names cannot contain `/` or NUL; `:` and `/` in a typed title are
  normalised to `-` on the Web (documented difference).
- Frontmatter stays at the very top of the file. Parsing follows MiaoYan's
  `MiaoYanCore.Frontmatter`: only a leading `---` block that looks like YAML
  (`key: value` lines, continuations, comments) is stripped; a `---` deck
  opener without YAML keys remains content.
- **Imported/edited MiaoYan notes are never rewritten just to add IDs.** The
  index owns the `path ↔ id` mapping. Only notes created through the API get a
  managed frontmatter header (`id`, `source_url`, `tags`, `created`, `updated`).
- Deletes move to `.miaoyan/trash/<timestamp>-<name>/` with a `meta.json`
  recording the original relative path. Restore puts it back (or beside it if
  the path is taken). Nothing is unlinked directly.
- Writes are atomic: write `.<name>.miaoyan-tmp-<uuid>` in the same directory,
  `File.Move(tmp, target, overwrite: true)`, retry on transient sharing errors.
  Per-note `SemaphoreSlim` serialises writes.
- Symlinked directories inside the vault are supported only when their
  resolved target stays inside the vault root; otherwise they are skipped and
  logged. This prevents traversal outside the library.

### Note identity

- API-created notes: `id` from frontmatter; `id` is stable across renames.
- Everything else: deterministic `n<12-hex>` from SHA-256 of the relative path
  at first index time. The index keeps the mapping so renames preserve the ID.
  If the SQLite index is deleted, imported notes get new IDs (documented; their
  Markdown is untouched).
- API IDs accept `[A-Za-z0-9][A-Za-z0-9._-]{0,119}`. Reserved: `..`, `.`,
  names containing `/`, `\`, control characters.

## 4. SQLite index (disposable)

State DB: `{state}/index.db`, WAL mode, FTS5 enabled by the bundled SQLite.

```sql
projects(rel_path TEXT PRIMARY KEY, name TEXT, created_at TEXT, sort_index INT);
notes(
  id TEXT PRIMARY KEY,
  rel_path TEXT NOT NULL UNIQUE,
  project TEXT NOT NULL,              -- "" for root notes
  title TEXT NOT NULL,
  excerpt TEXT NOT NULL,
  tags TEXT NOT NULL DEFAULT '[]',    -- JSON array
  kind TEXT NOT NULL DEFAULT 'note',
  created_at TEXT NOT NULL,           -- ISO-8601 UTC
  updated_at TEXT NOT NULL,
  mtime_ticks INTEGER NOT NULL,
  size INTEGER NOT NULL,
  content_hash TEXT NOT NULL,         -- sha256 of bytes
  pinned INTEGER NOT NULL DEFAULT 0,
  archived INTEGER NOT NULL DEFAULT 0,
  source_url TEXT NULL,
  body_loaded INTEGER NOT NULL DEFAULT 0
);
links(src_id TEXT NOT NULL, target_title TEXT NOT NULL, target_id TEXT NULL, ordinal INT);
categories(name TEXT PRIMARY KEY, created_at TEXT);   -- Papyra tag registry
media(hash TEXT PRIMARY KEY, filename TEXT NOT NULL, rel_path TEXT NOT NULL,
      mime TEXT, size INTEGER, created_at TEXT);
versions(note_id TEXT, version_id TEXT, rel_path TEXT, size INTEGER,
         hash TEXT, created_at TEXT);
notes_fts USING fts5(rowid, title, body, tags, tokenize='trigram');
```

- FTS uses the **trigram** tokenizer for CJK substring search. Queries shorter
  than 3 characters fall back to a title/excerpt `LIKE` scan (cheap on the
  indexed metadata) plus a body scan only when needed.
- The index is updated on every write and on watcher events. A full rescan runs
  at startup if the DB is missing or the vault mtime marker changed.
- `GET /api/healthz` reports note count, last scan time, FTS availability, and
  the resolved paths (never secrets).

## 5. HTTP API

### 5.1 Web API

| Method | Path | Purpose |
|---|---|---|
| GET | `/healthz`, `/api/health` | health + resolved vault/state |
| GET | `/api/projects` | projects with note counts |
| POST | `/api/projects` | `{name}` create folder |
| PATCH | `/api/projects/{path}` | rename / move |
| DELETE | `/api/projects/{path}` | move folder to trash |
| GET | `/api/notes` | list: `project`, `q`, `sort=updated\|created\|title`, `limit`, `offset` |
| POST | `/api/notes` | create `{title?,project?,body?}` |
| GET | `/api/notes/{id}` | note + body + ETag |
| PUT | `/api/notes/{id}` | save `{title?,body,tags?,pinned?}`; `If-Match` |
| PATCH | `/api/notes/{id}` | rename / pin / move / tags without touching body |
| DELETE | `/api/notes/{id}` | trash |
| POST | `/api/notes/{id}/conflict` | persist an in-editor draft as a conflict backup |
| GET | `/api/notes/{id}/backlinks` | backlinks + context |
| GET | `/api/notes/{id}/links` | outgoing wikilinks |
| GET | `/api/notes/{id}/versions` | version list |
| GET | `/api/notes/{id}/versions/{versionId}` | version content |
| POST | `/api/notes/{id}/versions/{versionId}/restore` | restore (snapshots current first) |
| POST | `/api/notes/{id}/attachments` | multipart attachment, returns URL + Markdown |
| GET | `/api/notes/{id}/assets/{kind}/{name}` | serve note-relative `i/`/`files/` |
| GET | `/api/search?q=&project=` | FTS search with snippets |
| GET | `/api/events` | SSE: `note-changed`, `note-created`, `note-deleted`, `project-changed`, `index-ready` |
| GET/PUT | `/api/settings` | server-side defaults (theme default, max upload, PlantUML availability) |
| POST | `/api/render/plantuml` | `{source}` -> SVG; 501 when local renderer absent |
| POST | `/api/import` | multipart zip of a MiaoYan vault |
| GET | `/api/export/notes/{id}?format=md\|html` | single note export |
| GET | `/api/export/vault?format=zip` | whole-vault zip |
| GET | `/api/trash` / `POST /api/trash/{entry}/restore` | recycle bin |

ETags are strong: `"sha256:<hex>"`. `If-Match: *` is accepted. A stale
`If-Match` returns **412** with `{error:"precondition-failed", currentEtag,
currentBody, conflictPath}`; the Web client then shows the conflict dialog.

### 5.2 Papyra-compatible subset (must work unchanged with `papyra-ingest`)

| Method | Path | Behaviour |
|---|---|---|
| GET | `/api/notes` | array `{id,title,tags,updated,excerpt,...}` |
| GET | `/api/notes/{id}` | `{id,title,body,tags,created,updated,kind,...}` + `ETag`; body **without** our managed frontmatter |
| PUT | `/api/notes/{id}` | `{title,tags,body,color,pinned,archived,kind}`; `If-Match`; create when absent (201); 412/409 on mismatch; `?archive=false` accepted |
| POST | `/api/media/upload` | multipart `file` -> `{filename,url,...}`; hash dedupe |
| HEAD/GET | `/api/media/{filename}` | existence / bytes |
| GET | `/api/search?q=` | `[{id,title,snippet,score}]` |
| POST | `/api/categories` | `{name}` -> register tag; idempotent |
| GET | `/note/{id}` | redirect/Serve SPA note route |

Media normalisation on write:

- `/api/media/<name>` and `![[<name>]]` are resolved to real files.
- Images become `/i/<name>`; other files become `/files/<name>`.
- Managed ingest markers (`rendered.py` zero-width START/END and the legacy
  `<!-- papyra-ingest:start -->`) are preserved verbatim; only media URLs inside
  the note are normalised, and frontmatter is managed separately.
- Repeated publishing updates the same note via its ID and preserves user text
  outside the managed block.

Auth for machines: `X-API-Key: <MIAOYAN_API_KEY>` when configured. If no key is
configured, machine endpoints remain open only when no web password is set
(LazyCat gateway / private LAN model); this is documented as a deployment
choice.

## 6. Editor and preview

### Editor (CodeMirror 6)

- Markdown highlighting, line wrapping, active line, bracket matching, search &
  replace, undo/redo, multi-cursor.
- Theme mirrors MiaoYan's palette: headings `#5E3DB3`, emphasis/HTML gold
  `#9A7400`, links `#24837B`, markup `#7A7872`, body `#403E3C` (dark variants
  in `web/src/lib/theme.ts`).
- Split modes: editor-only, preview-only, split (default split on desktop).
  `Cmd/Ctrl+\` toggles split; `Cmd/Ctrl+3` toggles preview.
- Autosave 600 ms debounce; visible states 未保存 / 保存中 / 已保存 / 冲突.
- Typewriter mode keeps the caret vertically centred; focus mode dims the
  sidebars and centres the writing column; font size, line height, and column
  width are adjustable and persisted.
- Toolbar: bold, italic, strikethrough, inline code, link, image, table, task,
  math, Mermaid, PlantUML, Markmap, PPT separator, fold block, heading.
- Paste/drag: images to `i/`, other files to `files/`; upload with progress;
  insert `/i/<name>` / `/files/<name>` (images as `![](/i/...)`, audio as
  `![[name]]`).
- Slash templates (`/table`, `/task`, `/mermaid`, `/plantuml`, `/markmap`,
  `/fold`, `/img`, `/video`, `/time`) expand on Tab, as in MiaoYan.
- Word count / reading time (Chinese-aware: CJK chars + Latin words), TOC.
- Shortcuts: `COMPATIBILITY.md` is authoritative. Browser-reserved combinations
  get `Ctrl/Cmd+Alt` aliases and are listed in the in-app help.

### Preview (client-side)

- markdown-it (CommonMark + GFM tables, task lists, footnotes, strikethrough,
  autolinks), highlight.js for fenced code.
- KaTeX inline `$...$` / block `$$...$$`.
- Mermaid and Markmap render in the browser, theme-aware, lazily when visible.
- PlantUML fences render through the **local** backend; if the jar is missing,
  the block shows a clear "本地 PlantUML 未启用" notice and never calls a public
  service.
- GitHub alerts (`NOTE/TIP/IMPORTANT/WARNING/CAUTION`), `<details>/<summary>`,
  image lightbox, responsive tables, inline audio player for `![[file.m4a]]`
  and audio links.
- Raw HTML is sanitised with DOMPurify: scripts/iframes/forms/objects removed;
  `details`, `summary`, `audio`, `video`, `source`, `img`, `table`, and inline
  `style` on non-script elements allowed. CSP is the second layer.
- Frontmatter is hidden in preview/export exactly as MiaoYan does.
- Split-view scroll sync is line-based (`data-source-line` from token maps):
  editor -> preview and preview click -> editor.
- Print stylesheet supports "Save as PDF" through the browser; no headless
  Chromium is bundled.

## 7. Filesystem watching, conflicts, versions

- One `FileSystemWatcher` (recursive, 500 ms debounce) per vault.
- Our own writes register `(fullPath, hash)` in a suppression set; the watcher
  event is dropped if the on-disk hash matches an acknowledged write.
- SSE event payload: `{type, id, path, title, etag, project, at}`.
- If the note is **not dirty**, the client quietly reloads it (cursor anchored
  by line where possible).
- If the note **is dirty** and the disk changed:
  1. The client POSTs its buffer to `/api/notes/{id}/conflict`; the server writes
     `.miaoyan-conflicts/<project>-<note>-<yyyyMMdd-HHmmss>.md`.
  2. The client shows a dialog ("磁盘上的文件已变化，已备份你的版本") and offers
     "载入磁盘版本" / "保留我的版本（另存为副本）".
  3. If the user saves with a stale ETag, the server performs the same backup of
     the submitted body before returning 412, so no keystrokes are lost.
- Version snapshots are taken before every content-changing write; at most one
  per 60 s per note unless the hash differs from the last snapshot. Retention:
  newest 50 or 30 days, whichever is larger. Files live in
  `{state}/versions/<note-id>/<timestamp>-<hash8>.md` with `meta.json`.
- Diff view is client-side (`diff` package); restore creates a snapshot of the
  current content first.

## 8. PPT, export, media ingest

- PPT: split on `---` lines outside fenced code and frontmatter. Optional
  leading HTML comment config (`transition`, `backgroundTransition`,
  `slideNumber`, ...) parsed and applied to reveal.js. `.slide:` comments set
  per-slide attributes; `.element:` fragments are supported for list items and
  paragraphs; code line ranges use reveal's `[1|2-3]` syntax. Background
  iframes are disabled for privacy/security (documented).
- Export:
  - note -> `.md` (byte-faithful) or self-contained HTML (inline CSS, local
    images as data URLs where reasonable, remote images left as URLs).
  - whole vault -> `.zip` (`MiaoYan-export-<date>.zip`).
  - PPT -> print view with all slides stacked for "Save as PDF".
- Media ingest:
  - images: limit/MIME check, optional EXIF strip and thumbnail (off by
    default in v1; both are pure metadata operations with ImageSharp when
    enabled).
  - audio: stored in `files/`, inline player, optional transcription.
  - video: never stored; with `ffmpeg` present extract a mono m4a to `files/`,
    optional transcript. Without ffmpeg, reject with a clear message.
  - text/URL ingestion stays in `papyra-ingest`; we only provide the API.
- Optional OCR/Whisper: off by default, enabled by env command; runs as a
  background job, writes into a managed block or a sidecar `.txt` next to the
  media. Failures never block note saving.

## 9. LazyCat deployment

- Single container, `EXPOSE 5080`, non-root user, no external services.
- `Dockerfile` multi-stage:
  1. Node builds the SPA into `server/MiaoYan.Web/wwwroot`.
  2. .NET SDK 10 publishes the API with the SPA embedded.
  3. Runtime `aspnet:10.0` (Debian) + local PlantUML jar and headless JRE +
     ffmpeg; no public rendering calls.
- `lazycat/package.yml` (LPK v2 static metadata):
  - `package: cloud.lazycat.app.miaoyanweb`, semver `version`, zh-CN/en-US
    locales, MIT license, icon.
  - `permissions.required: [document.private]`; without the mount the app
    refuses to start rather than losing notes in the container rootfs.
- `lazycat/lzc-manifest.yml`:
  - `application.routes: /=http://miaoyan.cloud.lazycat.app.miaoyanweb.lzcapp:5080`
  - a `services.miaoyan` container with explicit `entrypoint: /usr/local/bin/entrypoint.sh`
    and `run_as: "1000:1000"` (application.image does not execute ENTRYPOINT).
  - env `MIAOYAN_STATE_DIR=/lzcapp/var/miaoyan`, `ASPNETCORE_URLS=http://+:5080`.
  - health check `curl -fsS http://127.0.0.1:5080/healthz`.
  - no-cache inject for HTML so upgrades never run a stale SPA.
- Store submission path: public image → `lzc-cli appstore copy-image` →
  rewrite `services.miaoyan.image` to `registry.lazycat.cloud/<user>/...` →
  `lzc-cli project build`. `lzc-build.embed.yml` is an offline fallback.
- Persistence contract: the library must survive container replacement,
  uninstall/reinstall and upgrades. Notes live in `/lzcapp/documents/<uid>/MiaoYan`;
  the state dir under `/lzcapp/var` is rebuildable. The README documents
  backup/restore of both and the export zip.
- Multi-arch: `docker buildx build --platform linux/amd64,linux/arm64`.
- Store requirements: valid semver, complete metadata/locales, installable,
  healthy within 5 minutes, persistent data, passwordless entry through
  LazyCat ingress, and every submitted image hosted under `registry.lazycat.cloud`.

## 10. Performance, security, testing

### Performance targets

| Metric | Target |
|---|---|
| 10,000-note library opens | list usable < 2 s; body loaded lazily |
| Search | < 150 ms typical |
| 100 KB note preview | < 100 ms after debounce |
| Note list | virtualised, 60 fps scroll |
| Large notes | > 1 MB: disable syntax highlighting / diagram auto-render, keep editing |

Mechanisms: metadata-only scan, lazy body loads, SQLite FTS, virtual list,
debounced render (250 ms), IntersectionObserver for diagrams, preview DOM reused
while typing (block-level updates), image `loading=lazy`.

### Security

- Path traversal: every API path resolves under the vault; `..`, absolute
  paths, NUL, and symlinks escaping the vault are rejected with 400.
- Upload MIME sniffing + extension allowlist + size limit; stored names sanitised.
- DOMPurify + restrictive CSP (`default-src 'self'`; remote images allowed via
  `img-src 'self' data: https:`) + `X-Content-Type-Options`, `Referrer-Policy`.
- API key / password compared in constant time; login rate-limited; logs never
  include keys, cookies or note bodies.
- Atomic writes, per-note locks, conflict backups before any overwrite.
- PlantUML is local-only by default; the settings page states this explicitly.

### Tests

- **Unit (.NET)**: frontmatter parsing/stripping, wikilink parsing, file-name
  sanitisation/uniqueness, path safety, ETag, media normalisation, FTS query
  builder.
- **Integration (.NET, WebApplicationFactory + temp vault)**: scan, CRUD,
  If-Match 412 + conflict file, rename/move, trash/restore, media upload/dedupe,
  Papyra API shapes, search, versions.
- **Frontend unit (Vitest)**: wikilink rendering, PPT splitter, frontmatter
  hiding, relative time, slug helpers.
- **E2E (Playwright)**: create/edit/autosave, external change, search, PPT,
  audio playback, drag image, API ingest.
- **CI**: `dotnet test`, `npm test`, `npm run build`, Playwright smoke.

## 11. Milestones and acceptance

### M0 — skeleton (this milestone)

Deliverables: repo layout; ASP.NET Core 10 minimal API with `/healthz` and path
resolver; React 19 + Vite + TS shell; Dockerfile; docker-compose; LazyCat
package/manifest/build files; README/LICENSE/PLAN/COMPATIBILITY; build scripts.
Acceptance: `dotnet build`, `npm run build`, server serves the SPA and
`/healthz`; Dockerfile builds (where Docker is available); `lzc-cli` lints the
manifest.

### M1 — vault + three-pane editor

Scan/import, projects and notes CRUD, three-pane UI, CodeMirror, split preview,
autosave, attachments, watcher/SSE, trash. Acceptance: import a real MiaoYan
folder; open/edit/save 1,000 notes; paste/drag an image and see it render;
delete/restore a note; external edit refreshes the open note. **M2 gate per
product owner: import + edit + attachment is the main line and must pass before
PPT/API work.**

### M2 — links, search, versions

Wikilink index, backlinks, unresolved links, FTS search, note-list sorting/pin/
move, versions/diff/restore, conflict backups. Acceptance: backlinks correct on
a 500-note library; search < 150 ms; stale-save conflict creates a backup file
and never loses text.

### M3 — rich preview and presentation

KaTeX, Mermaid, PlantUML (local), Markmap, alerts, lightbox, audio, PPT,
themes, focus/typewriter, TOC, export HTML/MD/PDF. Acceptance: render a note
exercising every fence; run a deck; export and reopen the HTML; PlantUML works
offline or reports clearly.

### M4 — Papyra compatibility and ingest

Implement the API subset, media normalisation, ETag conflict retry. Acceptance:
run real `papyra-ingest` with `PAPYRA_BASE_URL`/`PAPYRA_API_KEY` against X and
Bilibili sources; create and update the same note; images/audio display;
re-publishing does not duplicate notes.

### M5 — optional features and release

Optional OCR/Whisper/video-audio, performance and security hardening, lpk
release, docs, backup/restore. Acceptance: 10k-note perf targets; security
checklist; installable `.lpk`; docs complete.

## 12. Working agreement

- Plan first, then code; small commits per milestone.
- MiaoYan's actual behaviour wins on product trade-offs. Anything not
  reproducible in a browser is documented with a Web substitute.
- Do not block on repeated confirmation for design details covered here; make a
  reasonable decision and record it in this file.
- Every milestone ends with tests, a runnable demo, updated docs, and a commit.

## 13. Decision log

| Date | Decision | Why |
|---|---|---|
| 2026-11-05 | New implementation, MIT license | Only architecture/API shapes referenced; no Papyra/MiaoYan code copied |
| 2026-11-05 | Filesystem is authoritative; SQLite index disposable | Matches MiaoYan's "notes are plain files" promise |
| 2026-11-05 | API-created notes get managed frontmatter; imported notes untouched | Required for stable IDs without rewriting user data |
| 2026-11-05 | Conflict backup on stale If-Match, return 412 | papyra-ingest retries on 409/412/428; user text must not be lost |
| 2026-11-05 | PlantUML local jar only; no public server | Note content privacy |
| 2026-11-05 | Single library, optional password + API key | v1 scope; LazyCat gateway provides user isolation |
| 2026-11-05 | Browser shortcuts use `Cmd/Ctrl+Alt` aliases where the browser reserves the combo | Same tasks reachable in a tab |
