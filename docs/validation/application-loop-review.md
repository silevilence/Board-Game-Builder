# 小核心阶段调度验收

日期：2026-10-28。范围仅为阶段一前三项；后续加载、桌游 update/render、Player／Editor 迁移和整组联合审核尚未完成。

- 迁移前基线与兼容矩阵见 [application-compatibility.md](application-compatibility.md)。七个示例、各三个固定种子共 21 局，提取宿主后再次运行 `application-baseline` 全部匹配原摘要。
- `dotnet run --project tests/Application.Tests` 通过。该项目只引用 Application.Core，检查公共程序集不依赖 Kernel、Kernel.Abstractions、Editor 或 MonoGame。包含显式阶段顺序、独立依赖图、顺序环、跨阶段／作用域错误、纯接口提供者、可变／固定逻辑时间、追赶限额、暂停／单步、输入复制与去重、两视口、绘制故障隔离、准备失败与提交故障的不同展示策略、慢任务关闭后的迟到结果、重复启停、启动失败清理、子作用域初始化中关闭及设备／实例／视口逆序释放。
- `Engine.Tests runtime-host cordi-dispatch startup runtime-entry runtime-recording` 通过，保留 CordiSharp 服务、资源与 UI 同步上下文隔离回归。
- `dotnet build BoardGameBuilder.slnx --no-restore` 通过，零警告、零错误。原 RuntimeHost／Scope／Definition 由 Runtime 程序集转发到 Application.Core，启动身份类型由 Kernel.Abstractions 转发到 Application.Abstractions，既有源代码无需改名。

调度器不代替插件自己的事务：准备阶段必须保持可见状态不变，返回的提交动作必须原子且不抛异常；违反提交契约会停止推进和绘制。StopAsync 等待不合作的回调退出，不承诺强制终止任意 .NET 代码。真实图形线程、桌游循环接入、不同帧率下实际游戏交互及干净发行环境仍需后续任务验收，以上测试不替代它们。
