# MonoGame DesktopGL 显式窗口补丁

上游固定为 [MonoGame 3.8.4.1 对应提交](https://github.com/MonoGame/MonoGame/tree/f34200720b558125964273fd3e7ab44cde0b0429) `f34200720b558125964273fd3e7ab44cde0b0429`。保留原 MS-PL [许可证](LICENSE.txt)。本目录只保存补丁、来源和构建脚本，不复制完整源码，不替换 MonoGame 技术选型。

`desktopgl-explicit-context.patch` 的当前 SHA-256 是 `46A421B691E8CC3E130BA0FC89608AACE4C8586D83AE0E51E1EAC4CD9D86A516`。历史原型使用的第一版补丁身份保留在其验证记录中。

补丁为每个 GraphicsDevice 使用 `PresentationParameters.DeviceWindowHandle`（DesktopGL 下是 `SDL_Window*`，不是 HWND），提供 `GraphicsAdapter.FromSdlWindow` 和显式 `GraphicsDevice.MakeCurrent()`。设备的顶点属性缓存改为实例字段；context 在所属图形线程立即释放；构造失败释放已创建 context。普通 Game 的 GraphicsDeviceManager 在 SDL 窗口重建后更新 handle。所有资源操作均须在同一个图形线程、先 MakeCurrent，再创建／使用／释放资源。

构建并验证：

正式 Avalonia 接入还会使用 VectorConversion 类型转换器。本补丁为 17 个内置 IPackedVector 类型提供静态构造，保留其原有打包语义；普通运行的自定义类型仍使用受动态代码保护的原路径，原生运行对未知类型明确返回不支持，不通过整库保留绕过分析。正式后端与实际游戏验证见 [集成记录](../../docs/validation/desktopgl-integration.md)。

```powershell
pwsh -NoProfile -File third_party/MonoGame/build-patched.ps1
pwsh -NoProfile -File tools/AotProbes/GlEmbedded/run.ps1
```

构建脚本要求 Git、SDK 和可访问的上游仓库；NativeAOT 探针另需 Windows C++ 工具链。脚本固定源码提交和 image decoder 子模块，核验补丁，拒绝混入其他源码修改；产出 DLL、哈希清单与完整 source-build.log。源码全库分析仍包含未消费 API 的上游警告，实际探针须独立通过严格 AOT publish；本补丁不声称所有 MonoGame API 获得认证。

维护升级时必须重新审阅每个 hunk、更新固定提交和补丁哈希，再复跑独立 Game、嵌入多视口及实际后端回归，不能只让 `git apply` 成功。当前实验和限制见 [嵌入实证](../../docs/validation/native-aot-gl-embedding.md)。
