# 分页文档呈现

内置运行插件 `resources.documents` 使用 `{ "path": "documents.json" }`，依赖 `resources`。
它提供纸张图片、分页和公开状态字段叠加，不执行规则、不保存另一份游戏记录。
文档 JSON 通过现有 `files.write` 或文件编辑保存，`validate` 校验引用，打包／导出自动携带。

```json
{"format":1,"panels":[{"id":"notes","title":"记录","permanent":true,"width":280,"pages":[
  {"title":"第一页","image":"paper","width":1000,"height":1600,"fields":[
    {"x":100,"y":200,"size":28,"text":"数量：{value}","binding":{"entity":"counter","property":"value"}},
    {"x":100,"y":260,"size":28,"binding":{"entity":"flag","property":"value","hideZero":true,"nonzeroText":"✓"}}
  ]}
]}]}
```

`image` 引用 `resources.images` 的完整图片（首版不支持图集区域）。坐标和字号使用页面坐标，呈现按比例缩放。
字段可为静态 `text`，或绑定实体整数属性；`values` 字典可将数值映射为游戏定义的文字。
`hideZero` 隐藏零值，`nonzeroText` 显示固定标记，`color` 是 `#RRGGBB`。隐藏实体／缺失投影字段不显示。
页面不会读取权威私有状态；作者校验只检查声明的实体／属性存在。

Player 的永久文档常驻桌面侧边，其他文档通过按钮在独立窗口打开／关闭，保持永久文档可见。
翻页位置仅属于当前视图，刷新不重置，结束对局仍可操作；关闭游戏释放图片与附属窗口。
当前支持单席位 `PlayBoard`，本次不扩展多人同屏布局。

`player.presentation` 二进制 JSON 可选 `width`（800～3840）及 `height`（600～2160）指定初始窗口尺寸，
Player 按实际工作区限制大小，用户仍可调整。未设置时保留默认尺寸。

可选 `statusEntity` 指向公开实体：Player 操作栏显示该实体当前面的文字，替换 `{属性名}` 为其整数属性。
这使当前操作提示由游戏配置／脚本提供；隐藏实体不显示文字，宿主故障仍优先报告。
