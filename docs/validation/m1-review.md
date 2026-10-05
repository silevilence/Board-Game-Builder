# M1 整体审核与验收记录

日期：2026-10-05。基线 `3bd75fcd9f5490f7d219868055be51af94a675af`。
需求仅采用 ROADMAP.md M1；不修改历史设计，不提前开展 M2 编辑器工作流或联机。

## 交付状态

前五项已实现、快速审核后原位勾选并各自提交：

| 项目 | 提交 |
| --- | --- |
| 双宿主 MonoGame 视口 | fd6d696 |
| 按席位投影的桌面 | 6b5fe6a |
| 交互与权威提交 | 72ec3f6 |
| 图形单人流程 | 53597d4 |
| 模块包与依赖校验 | 1847ccd |
| 自包含发行与导出实现 | ba961f2 |

第六项的两项实现子条目已完成，顶层仍未勾选：真实干净 Windows 验收没有完成。
工具在包含 Windows Sandbox 启动的预检命令上返回自动审批拒绝（blocked by policy），没有进一步原因；未尝试绕过。
本机已安装 .NET，移除子进程 PATH/DOTNET_ROOT 只能证明自包含运行路径，不能声称替代干净系统。

最终发行目录：`artifacts/player-release-reviewed`。
最终可玩导出目录：`artifacts/quick-cards-reviewed`，入口 `Play.cmd`。
沙箱配置：`artifacts/sandbox-reviewed/verify.wsb`，只读映射游戏、单独可写映射证据、禁用网络。
按 docs/m1-distribution.md 核对干净环境记录及双击入口交互后，才可勾选最后一项。

## Standards

按 code-review 技能独立审核 AGENTS.md 与 docs/agents 规范，没有明确规范违例。
保持 C#／Avalonia／MonoGame／Lua 选型，Kernel 没有 UI 或渲染依赖。

发现并修复三项独立正确性问题：

1. P1：超过 150 字符的标签先以长字符串查询、再以截断字符串存储，逐帧覆盖纹理且未释放。现在先归一化；真实设备冒烟重复绘制长文本，检查实际纹理创建次数为 1。
2. P2：自由对象的布局偏移被再次作为权威坐标提交。现在将拖拽增量加到原始实体坐标；回归核对命令与画面移动增量相同。
3. P2：设备失败只停渲染、未传给 Player。现在向宿主传递错误，停局并显示重新开始的提示。

判断性气味：输入和操作类型使用协议字符串，后续可改为各层的小型类型；本轮未为此进行无关重构。
修复复核没有新阻塞；初始 0 项明确违规、3 项正确性问题（已修复）、1 项非阻塞气味。

## Spec

独立规格审核同样发现长标签泄漏与自由拖拽坐标两项问题，已修复。
另指出真实干净 Windows 验收缺口，已明确保留未完成；没有发现明确范围扩张。
初始 2 项正确性问题（已修复）、1 项外部验收缺口；代码问题最高 P1。

## 主审补充修复

- CPU 原始异常可能含其私有信息，普通 Player 只接收脱敏故障；用 PRIVATE_CPU_CARD 标记验证不会进入 SessionFrame。
- 无可执行动作时给出 no-actions 故障，避免看似正常但无法继续的会话。
- 初始相机采用宿主 DPI；生成文字纹理采用正确透明混合。拖拽提供临时影像，拒绝后保持原始投影。
- HWND 增加 SS_NOTIFY，避免 STATIC 默认的透明命中；失焦取消交互，销毁同时解除滚轮订阅。
- 解压按实际读取字节执行上限校验；文件清单路径再次检查。
- 更新 CLI 帮助，覆盖 gui、pack、export 和 .bgm 输入。

## 验证与证据

Release 解决方案构建通过（0 警告、0 错误），工程边界检查通过。
14 组完整回归通过：packages、interaction、view、modules、commands、visibility、replay、turns、lua、sandbox（Lua 执行沙箱）、cpu、sample、batch、review。
最后视口输入与工具栏调整另经实际窗口验证；这里的 Lua sandbox 测试不等于 Windows Sandbox 验收。

- 实际 Player 图形界面从加载、开局、选动作、CPU 自动推进到结算，截图在 `artifacts/m1-validation/player-*.png`。
- 导出物实际拖拽合法手牌触发出牌及 CPU；非法拖回牌库后局面、计分和回合保持不变，有拒绝提示。证据 `export-drag-result.png`、`export-rejected-result.png`。
- Player／Editor 均进行三次真实图形设备绘制、尺寸变化和销毁，活跃设备回到零；修复后长文本纹理创建数检查通过。结果 `*-viewport-final.json`、`player-reviewed-viewport.json`。
- 图形重开两局最终投影完全相同；最终发行物在 PATH 无 dotnet、DOTNET_ROOT 指向不存在位置时同样通过。结果 `export-reviewed-games.json` success=true、games=2。
- 预编译导出物种子 1..20 全部终局，每局 7 次提交，独立重跑与日志回放一致。摘要见 [m1-seeds.json](m1-seeds.json)，完整材料 `artifacts/m1-export-batch`。
- 包与目录加载指纹、终局、回放相同；篡改文件、路径逃逸、依赖缺失／重复／版本错误均被拒绝。未引用作者文件与 editor_only 声明不进入包。
- 相机右键平移和缩放按钮实际通过；`button-zoom-before.png`／`button-zoom-after.png` 保存缩放前后对照。

## 已知限制

仅验收 Windows x64。原生视口有 airspace 限制，其他 UI 位于视口外。
滚轮由原生消息和宿主路由处理；当前 Cua 前台滚轮自动化未观测到缩放，未将其记为通过。
缩放已通过显式按钮验收，建议当前使用“放大／缩小”；物理滚轮仍需人工复核。
音效为可选的开关式轻提示，音频设备异常会静音，不改变规则结果。
图形库没有切换备用渲染器；设备失败明确报错。第三方运行时插件仍属 M3，M1 只接受官方依赖。

最终状态：实现与整体代码审核已完成；M1 完整验收仍被第六项干净环境测试阻塞。
