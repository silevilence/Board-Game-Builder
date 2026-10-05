# 模块包与运行时依赖

`Player pack <模块目录> <新文件.bgm>` 创建 ZIP 格式的数据包；目标已存在时拒绝覆盖。
`Player gui <目录或.bgm>`、`play`、`batch`、`replay` 共用 ModulePackage.Load。
包解压到唯一临时目录，校验后使用同一个 DirectoryModule；资源已加载到内存，临时目录随即删除。

`package.manifest.json` 包含 format、API、来源（模块 id@version）、模块内容指纹、各文件 SHA256 和运行时依赖。
拒绝缺失／额外文件、重复路径、越界路径、内容哈希错误、API／依赖版本不兼容。
限制 2048 个数据文件、每文件 32 MiB、总计 128 MiB。哈希用于完整性检测，不宣称发行者身份签名。

module.json 可增加 `api: 1`、`runtimeFiles: ["data/example.json"]` 和：

```json
"plugins": [
  {"id":"lua", "version":"2.0.0", "api":1},
  {"id":"author-panel", "version":"1", "api":1, "editor_only":true}
]
```

M1 官方依赖为 kernel 1、lua 2.0.0、view.monogame 3.8.4.1，清单自动补齐。
未知运行时插件明确失败；第三方 WASM 仍属 M3。runtimeFiles 是只携带、不执行的数据内容。
打包只收集 module.json、规则／CPU 入口、引用资源及显式 runtimeFiles；editor_only 声明被剥离，不递归复制作者目录。
不要将编辑器文件列为 runtimeFiles。目录与包的指纹忽略 editor_only 声明，并保持对局／回放一致。

卡面 resources 应为空或与 names 一一对应。投影只传递允许观察的资源标识；场景只收集投影实际引用的字节。
不存在或不能解码的图片在桌面显示提示。规则、API 与媒介数据仍使用现有 Kernel 权威校验。
