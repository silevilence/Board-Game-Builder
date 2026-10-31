# NativeAOT GL 精简证据

2026-10-31 从 `artifacts/native-aot/clean-evidence` 归档。环境为新 Windows Sandbox，OS 26100；未发现 dotnet 命令或 x64/x86 系统 .NET shared 目录。网络关闭，只映射隔离的只读探针输入与证据输出。

- `environment.json`：原始环境记录。
- `standalone-*`：未修改的 DesktopGL NuGet 包运行 `tools/AotProbes/Gl`，三轮独立 Game 窗口，退出 0。
- `embedded-*`：显式 context 补丁运行 `tools/AotProbes/GlEmbedded`，十轮双子窗口和失败路径，退出 0；每轮 GDI=13、USER=9。
- `embedded-survivor.png`：最后一轮第一视口关闭后，第二视口 backbuffer；中文和图片已查看。
- `hashes.json`：Sandbox 只读输入中实际 exe、原生库和资源的 SHA-256；PDB 调试符号未纳入精简清单。嵌入 exe 哈希与本机已验证产物相同。

标准 `GameRegression` 使用补丁 DLL 的本机回归记录留在完整 artifacts；它没有单独进入本次干净机归档。证据不覆盖正式 Avalonia、真实游戏或物理输入，详见 [GL 原型报告](../../native-aot-gl-embedding.md)。
