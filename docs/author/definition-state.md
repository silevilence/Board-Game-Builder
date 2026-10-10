# 显式状态定义格式

运行插件 `definition.state` 的 configuration.path 指定文件；作者插件 `author.state` 提供模板与序列化。`project.create.state` 可生成有效起点。

```json
{"format":1,"state":{"seats":[{"id":"p1","team":"one","controller":"human"}],"phases":["play"],"phase":"play","entities":{}},"resources":{}}
```

state 使用运行状态数据：seats、phases、phase、turn、currentSeat（从 0 开始的席位索引）、ended、winners，以及按 ID 索引的 entities 对象。省略可选状态字段时采用运行时默认值；不加入未知字段。entities 每项包含 id、owner、visibility、container、isContainer、capacity、ordered、contents、faces、faceResources、face、faceUp、x、y、properties、counterBounds、diceFaces 等按需字段；不使用组件格式的 components 数组。

`configuration.read` 返回 state 对象；`configuration.replace` 接受同一 state 模型，保留外层 format 和资源声明。资源通过入口插件加载，外层 resources 为空；身份和运行插件仅在 game.start.json 声明。两个格式共享规则和 CPU API，最终仍通过正式加载器验证。
