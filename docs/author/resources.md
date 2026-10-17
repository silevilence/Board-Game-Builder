# 图片、图集与资源整理

资源在 game.start.json 声明。resources 提供资源池；resources.images 读取 PNG/JPEG/GIF/BMP；resources.texturepacker 读取 TexturePacker JSON Hash。定义及视图按资源名称消费。

图片插件的 configuration 示例：

```json
{"sources":[{"path":"assets/face.png","name":"face"},{"path":"assets/ui","pattern":"**/*.png","prefix":"ui/"}]}
```

source 可为文件或目录；pattern 支持 `*`、`?`、`**`。目录省略 pattern 时只按该加载器支持的格式扫描。name 只适用于单个文件；目录默认名称为相对路径去扩展名加 prefix。资源名称不可重复。

`resources.import.image` 参数 source（外部图片）、target（新工程相对文件）、name（资源名），原样复制图片并更新入口。它不自动创建牌或游戏实体，也不重写动态脚本引用。

`resources.import.atlas` 参数 source（外部 JSON）、target（新工程 JSON 路径）、可选 name（帧名前缀）。图集的 meta.image 必须为安全相对路径，图片与 JSON 一同导入；帧名称由 frames 的键给出。支持 rotated、trimmed、spriteSourceSize、sourceSize。尺寸、裁剪区域、缺图、坏 JSON、重名和目标冲突由正式资源加载器拒绝。

`resources.move` 只自动移动单个显式声明的资源文件，更新源路径并保留资源名称。移动图集 JSON 跨目录时复制配套图片，旧图片保留以免影响其他声明；可检查引用后自行删除。目录、通配符发现或动态脚本路径不做猜测改写，操作会拒绝无法保持资源名集合的移动。

导入先验证隔离候选资源池，成功后通过所选编辑模式提交。相同请求 ID 重试不增加重复声明。图片文件默认单项最多 16 MiB；运行时另检查总预算、尺寸和图集区域。

## 随包字体和动画

`resources.font`（version `1`、api `1`）依赖 `resources`，configuration 为 `{"path":"assets/font.otf","license":"assets/OFL.txt"}`。选择一个静态 OpenType TTF／CFF OTF 作为游戏桌面及 Player 操作控件字体，字体和非空许可证都随包收集，不需要安装系统字体。缺件、格式错误或渲染缺字会报告诊断；切换／关闭视口释放字体缓存。配置使用现有 `files.write`，再运行 `project.validate` 和正式打包／导出。

`resources.animations` 的配置、作者操作和预览见 `bgb.exe docs animation`。字体／动画声明不能使用图片的自动移动命令；调整路径时需同步修改显式配置并重新校验。
`resources.documents` 可声明侧边分页文档及公开状态绑定，详见 `bgb docs documents`。
