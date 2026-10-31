# Windows x64 NativeAOT 构建服务

`Runtime.Publishing.NativeToolchain.Check` 和 `NativeBuildService.Build` 是无 UI 的作者侧 API。普通数据包和预编译 Player 导出不调用它们。首轮仅开放 Windows x64 本机构建，不把 Windows 上换 RID 当作跨系统支持。

工具链检查固定 SDK **10.0.112**、NativeAOT 运行库 **10.0.12**，检查 Visual Studio x64 C++ 编译器／链接器和 Windows SDK 的 UM／UCRT 库。构建输入中的 global.json 禁止 SDK 版本前滚；编译进程使用本次检查得到的工具目录和库目录，不另行挑选其他安装。缺 SDK、链接器、Windows SDK 或不支持目标都返回明确诊断，不降级到普通 Player。

构建只编译生成的专用入口与静态注册，引用已验证的预编译实现，不编译作者游戏源码。关闭外部 Directory.Build.props / targets 继承，使用严格 ILLink／ILC 警告处理，保存实际构建日志、生成项目／入口／注册、global.json、包锁和输入清单。运行包清单另记录工具版本、内容／插件身份、文件哈希和构建时间。

源内容进入独立临时输入目录；产物进入目标目录旁本次创建的暂存目录。成功后检查 EXE 为 x64 原生 Windows GUI 子系统，内容和原生依赖完整，且根目录没有托管程序集、coreclr 或调试符号，再原子移动到全新目标。已有输出不覆盖。取消会终止本次 dotnet 及其子进程，等待退出并清理拥有的临时目录；失败保留诊断日志，不留下可误认为成功的发行目录。

## 实际验收

```powershell
pwsh -NoProfile -File third_party/CordiSharp/build-static.ps1
dotnet run --project tests/Runtime.Publishing.Tests -- --native
```

2026-10-31 通过：

- 缺 SDK／C++ 链接器／Windows SDK 与 Linux 目标分别被预检拒绝。
- 外部 `sample.definition` / `sample.counter` 组合经过真实静态装配、NativeAOT 编译和运行。生成的 `Play.exe` 为 GUI 子系统，不闪控制台；中文及空格路径可运行，三次 update 得到 ticks=3，`dynamicCodeSupported=false`。
- 不存在的入口产生真实 C# 编译失败；故意使用 Assembly.LoadFrom 的替代入口产生 **IL2026**，错误声明不能跳过实际 AOT 分析。两次失败均没有最终目录。
- 在实际日志进入 `Generating native code` 后取消，构建被终止，没有最终目录或遗留暂存目录；之前成功的发行目录和全部源依赖哈希保持不变。
- 成功目录另复制并移动到没有源码、SDK、系统 .NET 和作者工具的 Windows Sandbox，运行退出 0、ticks=3、原生动态代码标志为 false。

报告位于 [evidence/native-build-service](evidence/native-build-service/)。此项验收构建服务和最小外部插件入口，正式图形游戏入口、作者导出和 Editor 闭环继续按各自任务验收。
