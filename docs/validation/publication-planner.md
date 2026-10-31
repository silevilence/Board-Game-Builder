# 发布依赖分析与静态输入

`Runtime.Publishing.PublicationPlanner` 消费已经由获准目录验证的完整 `GameContent`、明确的宿主插件选择和对应版本的发布依赖包。它不启动对局，也不读取一次运行的调用覆盖率。公共加载器已校验并捕获所有声明的定义预设；分析与输入复制保留整个快照，不只保留当前选中的预设。

每个选择精确匹配 ID、版本、API 和实现指纹；历史内置选择允许没有指纹，发布副本会固定到本次已验证实现。外部选择必须带指纹。源工程及原始 `GameContent` 不变，报告保留 `SourceFingerprint`；发布副本的新指纹反映已固定的实现身份，不伪称仍是未固定版本的源快照。

分析包括能力／版本／作用域／阶段顺序、图形宿主唯一后端、AOT 和目标声明、托管传递引用、原生文件架构、文件哈希及输出名称冲突。未选实现不会从安装目录自动加入。输入包中的执行器和原生库只有所属实现被选择时才进入编译集合；Lua／flow 的已知不兼容版本按内置实证声明拒绝。

无界面构建仍核验所选图形插件的身份和包内容，但静态注册只保留不可激活的描述，不引用其实现或原生库。有配置字段的停用图形描述通过可选 `contentFields` 元数据声明字段；缺此元数据会在预检阶段失败，不等游戏启动后才发现。普通激活时还会核对工厂与所声明字段一致。

`PublicationInputs.Create` 只接受新目录，复制后再次核对二进制哈希，生成直接工厂调用、相对运行库引用、完整内容和 `build-inputs.json`。取消或复制失败会清理本次创建的目录，现有目录不覆盖；源安装被替换或篡改不能改变已复制的输入。提供者和宿主的共用运行文件需显式传入，不能把整个安装目录当作默认依赖。

## 验证

```powershell
dotnet run --project tests/Runtime.Publishing.Tests -- artifacts/native-aot/wasm/production-board
dotnet run --project tests/Runtime.Plugins.Tests
dotnet run --project tests/Runtime.PluginImport.Tests
dotnet run --project tests/Runtime.StaticRegistration.Tests
pwsh -NoProfile -File scripts/check-boundaries.ps1
```

2026-10-31 全部通过：

- 外部非桌游组合保留未玩的预设；生成引用不含桌游、Lua、WASM 或 Editor。复制后直接以匹配目录加载，精确内容身份相同。
- 真实无脚本桌游计划只含六个显式基础插件；安装了 WASM 仍不带入其实现或原生库。真实 WASM 桌游计划包含 Wasm.Execution、Wasmtime 及固定哈希的原生库，不含 Lua 或作者 YAML 解析器。
- 版本缺失、指纹变化、重复选择、双后端、缺后端、缺托管传递引用、错误原生身份、输入篡改及取消均有拒绝结果。原生支持声明／依赖图 23 项和普通导入 22 项回归通过。
- 实际生成的桌游和非桌游项目编译运行，身份、能力、作用域及回调顺序与普通装配相同；无界面图形选择以仅身份描述保留。
- WASM 的独立静态工厂通过正式严格原生构建与干净 Windows 对局回放回归，结果与此前一致。

此项交付构建输入。SDK／链接器预检、实际构建服务、独立游戏入口和 Editor 发布仍分别验收。
