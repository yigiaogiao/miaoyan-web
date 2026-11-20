# `lzc-file-chooser-inject.js`（第三方产物，请勿手改）

| 项 | 值 |
|---|---|
| 来源 | <https://developer.lazycat.cloud/lazycat-injects/lzc-file-chooser-inject.js> |
| 取回时间 | 2026-11-18 |
| 大小 | 1 069 610 字节 |
| sha256 | `4da22e7d0b3167de57a63cb5f31af13a2e98292a17c344ca71bce70331d453b5` |
| 内含 | `@lazycatcloud/lzc-file-pickers` 2.1.3 的 UMD 打包（含 Vue 运行时） |

商店审核要求应用的文件入口接入懒猫统一文件选择器（「网盘文件 / 本地文件」两个页签）。
`lzc-manifest.yml` 的 `open-save-chooser` 注入会从这个路径加载脚本：

```text
file:///lzcapp/pkg/content/lazycat-injects/lzc-file-chooser-inject.js
```

因此 `lzc-build.yml` 设置了 `contentdir: ./content`，这个目录会打进 LPK 的
`content.tar`，挂载到 `/lzcapp/pkg/content`。侧载构建时
`scripts/build-sideload-lpk.sh` 会把它复制到 `lazycat/publish/lazycat-injects/`。

升级时重新从上面的地址取一份，更新本文的 sha256 和版本号。不要局部修改这个文件。
