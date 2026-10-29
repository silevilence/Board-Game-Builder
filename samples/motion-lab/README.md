# Motion lab

最小实时示例。方向键持续移动，抵达绿色目标后显示反馈，空格复位。

从仓库根运行 `dotnet run --project samples/Minimal.Player -- samples/motion-lab`。
入口也接受正式 `GamePackage.Pack` 生成的数据包。

`game.start.json` 显式选择内容、图片资源、更新及展示提供者；`motion.json` 保存标题、说明、速度与目标位置。玩法实现位于独立 Sample.Motion 程序集，宿主 ApplicationWindow 不识别示例身份。运行依赖不包含桌游状态、席位、回合、命令、Lua 或 WASM。

每个实例只有一个 ApplicationPump；视口通过输入队列提交输入，只读取不可变 MotionSnapshot 绘制。多个视口具有独立输入来源，失焦或关闭只撤销自己的持续按键。可控固定时间、暂停／单步、无界面与多视口验证见 Motion.Tests 和 Motion.ViewTests。此示例不承诺桌游记录回放或实时联机。

作者工具通过独立 Sample.Motion.Authoring 提供，author.profile.json 选择 author.motion。运行 `dotnet run --project src/Cli -- validate --project samples/motion-lab` 校验，`dotnet run --project src/Cli -- call configuration.read --project samples/motion-lab` 读取格式模型，`dotnet run --project src/Cli -- call run.application --project samples/motion-lab` 推进默认 60 个固定更新。作者配置不进入游戏运行包。

profile 同时选择 author.export.package 和 author.export.player。使用 `bgb.exe pack --project samples/motion-lab --args '{"target":"C:/Games/Motion.bgm"}'` 生成 format 3 数据包；独立 Player 使用 `bgb.exe export-player --project samples/motion-lab --args '{"target":"C:/Games/Motion","player":"C:/Releases/MinimalPlayer","offline":true}'`。输出须为新路径，预编译 MinimalPlayer 由维护者按 [发行说明](../../docs/m1-distribution.md) 准备；作者端无需 SDK。Play.exe 可从任意工作目录启动，移动完整目录后仍使用相邻 game.bgm。
