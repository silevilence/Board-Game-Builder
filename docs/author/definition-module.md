# 组件定义格式

运行插件 `definition.module` 的 configuration.path 指定 JSON 文件；作者插件 `author.module` 提供模板与结构化读写。游戏身份、脚本及资源插件配置位于 game.start.json，不在此文件重复声明。

最小定义：

```json
{"format":1,"resources":{},"seats":[{"id":"p1","team":"one","controller":"human"}],"phases":["play"],"entities":[]}
```

席位 id 唯一，controller 为 human 或 cpu；阶段非空且唯一。entities 是 `{id, components}` 数组，每个组件为 `{type,data}`，相同实体不重复组件类型。常用组件：

| type | data |
| --- | --- |
| owner | `{"seat":"p1"}` |
| visibility | `{"mode":"public"}`；其他受支持值 owner、hidden |
| position | `{"x":100,"y":100}` |
| properties | 整数属性，例如 `{"score":0}` |
| counter | `{"property":"score","min":0,"max":100}` |
| container | `{"capacity":10,"ordered":true,"contents":["card1"]}` |
| faces | `{"names":["Front","Back"],"resources":["front","back"],"index":0,"up":true}` |
| dice | `{"values":[1,2,3,4,5,6]}` |

容器内容列出实体 ID，不重复归属或形成循环。隐藏容器保留外壳和数量，身份投影不公开隐藏内容；不要在公开实体 ID、位置或外壳中编码秘密。牌面 resources 使用资源池名称，见 `docs resources`。

`configuration.read` 返回作者模型和来源版本；`configuration.replace` 用该版本和完整 configuration 模型序列化，拒绝版本冲突。也可直接编辑定义文件。运行校验负责引用、组件、容量及规则约束。
