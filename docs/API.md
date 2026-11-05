# API

所有接口同源，无需 CORS。除 `/healthz` 外，懒猫请求由网关注入受信任的 `X-HC-User-ID`；
独立部署可通过会话（M5）或 `MIAOYAN_API_KEY` 授权。错误统一返回 JSON：

```json
{ "error": "code", "message": "human readable detail" }
```

## 已实现（M0）

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

## Web API（M1–M3，规划）

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/projects` | 项目树，含笔记数量 |
| `POST` | `/api/projects` | 新建项目 |
| `PATCH` | `/api/projects/{id}` | 重命名/移动 |
| `DELETE` | `/api/projects/{id}` | 移入回收站 |
| `GET` | `/api/notes?project=&sort=&q=` | 元数据列表，正文懒加载 |
| `POST` | `/api/notes` | 新建笔记（UUID 文件名，显示为 Untitled Note） |
| `GET` | `/api/notes/{id}` | 正文 + 标题 + 修改时间 + ETag |
| `PUT` | `/api/notes/{id}` | 保存，`If-Match` 冲突返回 412 + 备份 |
| `PATCH` | `/api/notes/{id}` | 重命名/移动/置顶 |
| `DELETE` | `/api/notes/{id}` | 移入 `.miaoyan/trash` |
| `POST` | `/api/notes/{id}/restore` | 恢复 |
| `GET` | `/api/notes/{id}/backlinks` | 反向链接 |
| `GET` | `/api/links/unresolved` | 未解析 wikilink |
| `GET` | `/api/search?q=` | FTS5 搜索，返回片段 |
| `GET` | `/api/notes/{id}/versions` | 版本列表 |
| `GET` | `/api/notes/{id}/versions/{v}` | 版本正文 |
| `POST` | `/api/notes/{id}/versions/{v}/restore` | 恢复版本 |
| `POST` | `/api/assets` | 图片/附件上传，支持 `i/` 与 `files/` |
| `GET` | `/api/events` | SSE：文件/索引变化 |
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
- ID：API 创建的笔记在 frontmatter 写 `id`；导入笔记不写 frontmatter，使用路径哈希，
  移动后 ID 可能变化，客户端应以路径为准并在收到 410 后刷新。
- 写操作原子替换：同目录临时文件 + `File.Move`。
- 路径参数经过规范化和符号链接检查，任何逃逸出书库的路径返回 400。
- 上传大小上限由 `MIAOYAN_MAX_UPLOAD_MB` 控制。
