# 备份与恢复

妙言的核心理念是「笔记就是文件」，妙言 Web 保持这一点：备份首先是复制目录，不需要导出数据库。

## 备份什么

| 路径 | 必选 | 内容 |
|---|---|---|
| 书库根目录 | 是 | `.md`、`i/`、`files/`，以及 `.miaoyan/trash`、`.miaoyan/trash-assets`、`.miaoyan-conflicts` |
| `/data/state` 或 `/lzcapp/var/miaoyan` | 否 | SQLite 索引、版本历史、缓存。索引可重建；版本历史重建后丢失 |
| 环境变量 | 建议 | 密码、API key、上传上限等部署配置 |

懒猫上书库位于 `/lzcapp/documents/<uid>/MiaoYan`，每位用户一份。宿主机或微服文件管理器可以直接
访问该目录；不要把 `/lzcapp/var` 当成书库备份来源。

## 备份方式

### 停容器后复制（最稳妥）

```bash
docker compose stop
rsync -a --delete /srv/miaoyan/library/ /backup/miaoyan-library-$(date +%F)/
rsync -a /srv/miaoyan/state/ /backup/miaoyan-state-$(date +%F)/
docker compose start
```

### 运行中复制

笔记写入是原子的（临时文件 + rename），运行中复制通常安全；但 SQLite 索引可能处于 WAL 状态。
只备份书库时无需处理 SQLite。要备份版本历史，请在复制前执行：

```bash
sqlite3 /srv/miaoyan/state/index.db "PRAGMA wal_checkpoint(TRUNCATE);"
```

或直接使用 SQLite 在线备份：

```bash
sqlite3 /srv/miaoyan/state/index.db ".backup '/backup/index.db'"
```

### 导出

应用内导出：单篇 Markdown/HTML、PPT，以及侧栏的「导出书库 (zip)」（`GET /api/export/vault`）。
zip 便于迁移，但会省略 `.miaoyan*`（回收站、清理暂存）和 `.miaoyan-conflicts`，也不包含
状态目录里的版本历史，因此不替代完整目录备份。

## 恢复

1. 停止容器。
2. 把备份的书库内容放回 `/data/Miaoyan`（或懒猫的 `/lzcapp/documents/<uid>/MiaoYan`）。
3. 状态目录可以恢复旧索引，也可以留空让应用重建。
4. 确保 UID 1000 可写：

   ```bash
   sudo chown -R 1000:1000 /srv/miaoyan
   ```

5. 启动容器，访问 `/healthz` 确认 `status=ok`，再打开书库。

如果只恢复了笔记、状态目录为空，首次打开会重建索引；wikilink、搜索即可用，版本历史为空。

## 冲突备份

外部编辑器或同步工具在 Web 编辑期间改了同一文件时，保存会：

1. 不覆盖对方内容；
2. 把当前编辑器内容写入 `.miaoyan-conflicts/<note>-<timestamp>.md`；
3. 提示用户选择重新加载或保留副本。

冲突文件不会被自动删除，可随时人工合并。

## 回收站

`删除` 把笔记移入 `.miaoyan/trash/<timestamp>/` 并写入 `meta.json`（原路径、标题、删除时间）。
回收站不会自动清理，条目一直保留，直到你在应用内彻底删除或手动删掉 `.miaoyan/trash`。
恢复使用应用内操作；直接复制文件回原目录也可以，但索引会在下次扫描时更新。

「清理未使用附件」不会永久删除文件：未被任何笔记（含回收站笔记）引用的附件会被移到
`.miaoyan/trash-assets/<timestamp>/`，保留原来的相对目录结构，误清理时可以手工搬回。
该目录不会自动删除；确认不再需要后可手动清理，它也不会进入 zip 导出。

## 版本历史

每次保存前，当前文件内容会先存一份快照，再把新内容写入磁盘；恢复旧版本时也会先快照当前内容。
历史位于状态目录的 `versions/<note-id>/`，保留最新 50 个版本或 30 天内的版本。
重命名/移动导致笔记 ID 变化时，版本目录会一起迁移。删除状态目录会丢失历史，但不会影响笔记本身。

## 从妙言导入

M1 起支持直接指向一个妙言书库目录：

- 顶层文件夹 = 项目；
- `.md` 文件名 = 标题；
- `i/`、`files/` 原样使用；
- frontmatter、PPT 的 `---` 分页、`[[链接]]` 不做改写；
- 导入只读扫描，不会修改原文件（除非你在 Web 中保存）；
- 导入后即可搜索、查看反向链接和版本历史；锚点/别名式 wikilink 与图片说明不解析，按妙言的
  `[[精确标题]]` 规则处理。

建议首次导入前对原目录做一次快照，确认显示与链接无误后再切换日常使用。
