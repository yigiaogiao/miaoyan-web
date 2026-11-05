# API

所有接口同源，无需 CORS。除 `/healthz` 外，懒猫请求由网关注入受信任的 `X-HC-User-ID`；
独立部署可通过会话（M5）或 `MIAOYAN_API_KEY` 授权。错误统一返回 JSON：

```json
{ "error": "code", "message": "human readable detail" }
```

## 已实现（M0 / M1）

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
| `GET` | `/api/notes?project=&q=` | 笔记元数据列表；`project` 为空表示全部，`q` 匹配标题/正文 |
| `POST` | `/api/notes` | `{projectId?, title?}`；空标题使用大写 UUID 文件名，显示为 “Untitled Note” |
| `GET` | `/api/notes/{id}` | 正文 + 元数据 + `ETag` 响应头 |
| `PUT` | `/api/notes/{id}` | `{content, etag}`；`etag` 不匹配返回 412 和冲突 JSON；成功后返回新笔记 |
| `PATCH` | `/api/notes/{id}` | `{title?, projectId?}` 重命名/移动；目标存在返回 409；可能返回新 `id` |
| `DELETE` | `/api/notes/{id}` | 移入 `.miaoyan/trash`，返回 204 |
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
  "hasBom": false
}
```

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

服务端在返回 412 前已把磁盘当前内容备份到 `conflictPath`，不会覆盖任何一方。

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

### 计划中的 Web API（M2–M3）

| 方法 | 路径 | 说明 |
|---|---|---|
| `PATCH` | `/api/notes/{id}` | 在重命名之外增加 `pinned`、`sort` 字段 |
| `GET` | `/api/notes/{id}/backlinks` | 反向链接 |
| `GET` | `/api/links/unresolved` | 未解析 wikilink |
| `GET` | `/api/search?q=` | FTS5 搜索，返回片段 |
| `GET` | `/api/notes/{id}/versions` | 版本列表 |
| `GET` | `/api/notes/{id}/versions/{v}` | 版本正文 |
| `POST` | `/api/notes/{id}/versions/{v}/restore` | 恢复版本 |
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
