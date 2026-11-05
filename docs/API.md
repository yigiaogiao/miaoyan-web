# API

所有接口同源，无需 CORS。除 `/healthz` 外，懒猫请求由网关注入受信任的 `X-HC-User-ID`；
独立部署可通过会话（M5）或 `MIAOYAN_API_KEY` 授权。错误统一返回 JSON：

```json
{ "error": "code", "message": "human readable detail" }
```

## 已实现（M0–M2）

### `GET /healthz`

```json
{
  "status": "ok",
  "version": "0.1.0",
  "startedAt": "2026-11-05T11:05:25Z",
  "uptimeSeconds": 3,
  "vault": {
    "mode": "lazycat | explicit | development",
    "documentsRoot": "/lzcapp/documents",
    "explicitLibrary": null,
    "state": "/lzcapp/var/miaoyan",
    "userCount": 2,
    "defaultUid": null
  },
  "sqlite": { "version": "3.53.3", "fts5": true, "trigram": true },
  "capabilities": { "plantuml": true, "whisper": false, "ocr": false, "ffmpeg": true }
}
```

`/api/health` 302 到 `/healthz`。健康检查不解析用户，因此无需请求头。

### 用户与书库解析

`/api/*` 每次请求都解析一次书库：

- 独立部署：`MIAOYAN_LIBRARY` 指定的目录。
- 开发：未设置 `MIAOYAN_LIBRARY` 时使用 `dev-library/`。
- 懒猫：`X-HC-User-ID`（缺省回退 `MIAOYAN_DEFAULT_UID`）映射到
  `/lzcapp/documents/<uid>/MiaoYan`，并按需创建。

笔记 ID 由书库内相对路径哈希得到（`n` + 12 个十六进制字符）。重命名或移动会改变 ID；
客户端应使用响应里的新 ID 跳转，并对旧 ID 的 404 做刷新。

### 已实现的 Web API

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/vault` | `{name, root, noteCount, projectCount}` |
| `GET` | `/api/projects` | 项目列表（顶层文件夹，含递归笔记数） |
| `POST` | `/api/projects` | `{name}`，空名自动生成“新建项目 N” |
| `PATCH` | `/api/projects/{id}` | `{name}` 重命名；目标已存在返回 409 |
| `DELETE` | `/api/projects/{id}` | 整个文件夹移入 `.miaoyan/trash`，返回 204 |
| `GET` | `/api/notes?project=&q=` | 笔记元数据列表；`project` 为空表示全部，`q` 匹配标题/正文；置顶笔记排在最前 |
| `POST` | `/api/notes` | `{projectId?, title?}`；空标题使用大写 UUID 文件名，显示为 “Untitled Note” |
| `GET` | `/api/notes/{id}` | 正文 + 元数据 + `ETag` 响应头 |
| `PUT` | `/api/notes/{id}` | `{content, etag}`；`etag` 不匹配返回 412 和冲突 JSON；成功后返回新笔记 |
| `PATCH` | `/api/notes/{id}` | `{title?, projectId?, pinned?}` 重命名/移动/置顶；目标存在返回 409；可能返回新 `id` |
| `POST` | `/api/notes/{id}/duplicate` | 创建副本，返回 201 与新笔记；命名为 `Name Copy`、`Name Copy 2`… |
| `DELETE` | `/api/notes/{id}` | 移入 `.miaoyan/trash`，返回 204 |
| `GET` | `/api/links` | wikilink 索引：已解析目标与未解析标题 |
| `GET` | `/api/notes/{id}/backlinks` | 反向链接（来源笔记、行号、上下文片段） |
| `GET` | `/api/search?q=&project=&limit=` | FTS5 搜索；`limit` 默认 50，限制在 1–500 |
| `GET` | `/api/notes/{id}/versions` | 版本列表，最新在前 |
| `GET` | `/api/notes/{id}/versions/{versionId}` | 版本正文 |
| `POST` | `/api/notes/{id}/versions/{versionId}/restore` | `{etag?}` 恢复；当前内容先存为版本；返回恢复后的笔记 |
| `POST` | `/api/notes/{id}/assets?kind=image\|file` | multipart 字段 `file`；返回 `AssetDto` |
| `GET` | `/api/notes/{id}/assets/{**path}` | 读取笔记目录下 `i/` 或 `files/` 中的附件，支持 Range |
| `GET` | `/api/trash` | 回收站条目（笔记与项目） |
| `POST` | `/api/trash/{id}/restore` | `{kind, noteId?, projectId?}`；重名时恢复到副本名 |
| `DELETE` | `/api/trash/{id}` | 彻底删除，返回 204 |
| `GET` | `/api/events` | SSE；具名事件 `change` |

`NoteDto` 字段：

```json
{
  "id": "n3f2a1c0b9d8",
  "title": "会议/周报",
  "path": "工作/会议:周报.md",
  "projectId": "p71c0d2a4e5f",
  "projectName": "工作",
  "folder": "工作",
  "content": "# 周报\n...",
  "etag": "c1d2e3f4...",
  "modifiedAt": "2026-11-05T10:00:00+00:00",
  "createdAt": "2026-10-01T08:00:00+00:00",
  "size": 1234,
  "isUntitled": false,
  "pinned": false,
  "hasBom": false
}
```

列表项 `NoteSummaryDto` 与 `NoteDto` 相同，但没有 `content`、`etag`、`hasBom`，并以
`excerpt` 代替正文；两者都带 `pinned`。

`ETag` 由内容与修改时间的哈希计算，同时放在响应头（带引号）和 JSON 的 `etag` 字段里。
`PUT` 优先读取 JSON 里的 `etag`；`If-Match` 已预留，M4 的 Papyra 兼容层使用。

保存冲突（412）返回：

```json
{
  "error": "conflict",
  "message": "The note changed on disk.",
  "currentEtag": "9a8b...",
  "conflictPath": ".miaoyan-conflicts/工作/会议-周报-20261105-100000.md",
  "currentContent": "..."
}
```

服务端在返回 412 前已把本次提交的文本写入 `conflictPath`，磁盘上的当前内容放在
`currentContent` 里；两边都不会被覆盖。

附件上传返回：

```json
{
  "url": "/api/notes/n3f2a1c0b9d8/assets/i/20261105-100000-abc.png",
  "markdown": "![](i/20261105-100000-abc.png)",
  "name": "20261105-100000-abc.png",
  "size": 20480,
  "kind": "image"
}
```

`kind=image` 时文件进入笔记旁边的 `i/` 并返回图片 Markdown；其他文件进入 `files/`，
返回链接 Markdown。`url` 可直接用于 `<img>`/`<audio>`，且带 `private, max-age=3600`。

回收站条目：

```json
{
  "id": "20261105-100000-abcd1234",
  "kind": "note | project",
  "title": "会议/周报",
  "originalPath": "工作/会议:周报.md",
  "deletedAt": "2026-11-05T10:00:00+00:00",
  "size": 1234
}
```

SSE 事件：

```text
retry: 3000

event: change
data: {"kind":"note|project|trash","noteId":"n...","path":"工作/x.md","at":"..."}
```

服务端每 20 秒发送 `: ping` 保活；前端断开后 3 秒重连。

### 链接、反向链接与搜索

`GET /api/links`：

```json
{
  "targets": [{ "title": "周报", "noteId": "n523a27722adb", "projectName": "工作", "path": "工作/周报.md" }],
  "unresolved": [{ "title": "还没写的笔记", "count": 1 }]
}
```

`[[标题]]` 只在正文中统计，代码围栏与行内代码里的内容会被忽略；标题匹配区分大小写优先，
不区分大小写仅作为回退。未解析标题用于预览中的“创建笔记”动作。

`GET /api/notes/{id}/backlinks`：

```json
[
  {
    "noteId": "n4e08abd6b4ba",
    "title": "欢迎",
    "projectName": "未分类",
    "line": 6,
    "excerpt": "相关：[[欢迎]]"
  }
]
```

`GET /api/search?q=量子纠缠&limit=40`：

```json
[
  {
    "id": "n4e08abd6b4ba",
    "title": "量子纠缠",
    "projectId": "root",
    "projectName": "未分类",
    "snippet": "…关于量子纠缠的正文…",
    "modifiedAt": "2026-11-05T10:00:00+00:00",
    "size": 1234,
    "isUntitled": false
  }
]
```

三个及以上字符使用 FTS5 trigram；一至两个字符回退到 `LIKE`，`%`、`_`、`\` 会被转义。
标题精确匹配、标题包含匹配排在正文匹配之前。

### 版本

`GET /api/notes/{id}/versions`：

```json
[
  { "id": "638900000000000000-a1b2c3d4", "createdAt": "2026-11-05T10:00:00+00:00", "size": 1234, "etag": "a1b2…", "isCurrent": true }
]
```

`GET /api/notes/{id}/versions/{versionId}` 返回 `{id, content, createdAt, size, etag}`。
`POST …/restore` 接受 `{etag?}`；恢复前会把当前内容存为版本，`etag` 不匹配同样返回 412。
重命名或移动导致笔记 ID 变化时，版本目录会一起迁移；保留最新 50 个版本或 30 天内的版本。

### 计划中的 Web API（M3）

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/export/note/{id}?format=md\|html` | 单篇导出 |
| `GET` | `/api/export/deck/{id}` | PPT 导出 |
| `POST` | `/api/import` | 导入妙言书库 zip/目录 |

## Papyra 兼容 API（M4）

目标：现有 `papyra-ingest` 不改代码即可发布笔记。

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/notes` | 笔记/分类列表（兼容字段） |
| `GET` | `/api/notes/{id}` | 正文，返回 `ETag` |
| `PUT` | `/api/notes/{id}` | `If-Match` 条件写；缺失时创建；`?archive=false` |
| `POST` | `/api/categories` | 创建分类 |
| `GET` | `/api/search?q=` | 搜索 |
| `POST` | `/api/media/upload` | 返回 `{ "filename": "..." }` |
| `HEAD` | `/api/media/{filename}` | 存在性与类型 |
| `GET` | `/api/media/{filename}` | 媒体内容；图片以 `/api/media/<filename>` 引用 |
| `GET` | `/note/{id}` | 302 到 SPA 笔记路由 |

兼容细节：

- `PUT` 在 `409/412/428` 时客户端重试；服务端用 ETag 判定并保留冲突备份。
- 上传文件名由服务端生成，客户端必须使用响应中的 `filename`。
- `papyra-ingest` 的零宽 START/END 标记与旧式
  `<!-- papyra-ingest:start -->` 块会被合并，重复发布不产生重复段落。
- 缺失笔记的 `PUT` 视为创建，避免首次发布失败。

## 约定

- 时间：ISO 8601 UTC。
- ID：当前实现统一使用路径哈希；API 创建的笔记暂时不写 frontmatter 的 `id`。
  移动/重命名后 ID 变化，客户端应使用响应里的新 ID。
- 写操作原子替换：同目录临时文件 + `File.Move`。
- 路径参数经过规范化和符号链接检查，任何逃逸出书库的路径返回 400。
- 附件路径只允许笔记目录下的 `i/` 与 `files/`；文件名由服务端清洗，符号链接被拒绝。
- 上传大小上限由 `MIAOYAN_MAX_UPLOAD_MB` 控制。
- 错误统一为 `{error, message}`；保存冲突额外返回 `currentEtag`、`conflictPath`、`currentContent`。
