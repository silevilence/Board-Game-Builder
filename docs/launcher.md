# Windows 版本启动器

启动器与引擎分别安装。将完整启动器目录放在可写的本地位置，双击 `Launcher.exe`；不需要预装引擎、.NET SDK 或系统 Runtime。命令行入口为同目录的 `bgb-launcher.exe`。

## 本地版本登记

解压官方工具包后，在启动器填写其根目录并选择「登记已有工具包」。程序会核对版本清单、必需入口和文件哈希；同一产品版本不能登记两套冲突内容。版本列表分别显示可用、目录缺失与需要修复的状态。V0.1.0 显示为旧完整包，保留原有布局。

选择「设为默认版本」保存之后的版本选择。已有程序实例不会因默认值变化而改变。工程应放在自己的作者目录，不放进产品或启动器程序目录。

也可从命令行检查与登记：

```powershell
./bgb-launcher.exe status
./bgb-launcher.exe register 'D:/引擎/0.2.0'
./bgb-launcher.exe default V0.2.0
./bgb-launcher.exe settings
```

## 官方版本与离线缓存

点击「刷新 GitHub 版本」读取官方仓库的历史发行、包类型、运行时要求和发布说明；组件 Release 与源码归档不会成为产品选项。同版本重复来源、缺少附件或失效组件会显示具体原因。`bgb-launcher catalog refresh` 提供同一列表，`bgb-launcher catalog` 只读本地缓存。启动器启动时不会强制联网；网络失败或限流保留上次校验结果及本地安装，刷新也不会删除任何安装。

## 运行时模式

私有模式对应独立包，使用清单锁定的 Runtime 文件并跨版本共享相同内容。系统模式对应精简包，每次运行检查 x64 Core／Desktop 框架、最低补丁与 `LatestPatch` 策略；同主／次版本的较新补丁可用，不跨主／次版本，也不要求 SDK。系统 Runtime 缺失或被移除会报告错误，不自动切换模式。启动器不会修改系统环境变量。

GUI 可检查候选包的模式或查看已安装版本的实际解析。命令行使用 `runtime-plan V0.2.1 private`、`runtime-plan V0.2.1 system` 或 `runtime V0.2.0`；先刷新目录以取得候选清单。直接运行版本目录的 Editor／CLI／Player 使用同一策略，私有模式不依赖启动器设置临时环境。

## 安装与离线导入

首次安装前可用 `storage <目录>` 选择本地 NTFS 存储根目录。刷新版本后，在候选包中选择私有或系统模式安装；页面显示下载、校验和安装状态，并可取消。同一版本只登记一个模式，已存在的不同内容或模式不会被覆盖。

```powershell
./bgb-launcher.exe catalog refresh
./bgb-launcher.exe plan V0.2.1 private
./bgb-launcher.exe install V0.2.1 private
./bgb-launcher.exe import 'D:/下载/Board-Game-Builder-V0.2.1-standalone-win-x64.zip' 'D:/下载/Board-Game-Builder-V0.2.1-standalone-win-x64.zip.sha256'
./bgb-launcher.exe storage-report
```

GUI 的「导入本地 ZIP」要求同目录有同名 `.zip.sha256` 文件。独立 ZIP 可断网导入；精简 ZIP 导入前必须具备兼容系统 Runtime。V0.1.0 旧完整包可导入并按原布局运行，不会被自动解释成新共享格式。`storage-report` 测量新共享格式的物理身份与分配，旧布局不计入新格式统计。

安装在校验完整内容与运行时之后才原子登记。取消或失败保留旧版本，未完成安装不会出现在可用列表；重启 GUI 或下一次安装会恢复中断事务，也可运行 `recover`。设置或恢复身份冲突会报告错误并保留证据，不自动覆盖用户目录。CLI 安装可用 Ctrl+C 取消。

## 启动、修复与卸载

在已安装版本上选择「启动 Editor」，或运行 `launch V0.2.0`；不指定版本的 `launch` 使用当前默认。`run V0.2.0 -- docs entry` 调用该版 CLI，原样转发参数、输入输出和退出码；Ctrl+C 取消子进程树。直接运行安装目录内入口仍然有效。

`uninstall V0.2.0` 只删除启动器管理的产品目录并回收无引用内容；外部登记目录仅取消登记。删除默认版本后须重新选择，后续安装不替你指定另一个默认版本。工程请放在产品目录之外；版本目录包含未登记用户文件时卸载会停止并保留文件。正在运行的 Editor、CLI、Player 或工作进程会阻止对应版本卸载，包括直接启动和启动器重启后的进程；另一版正在使用的共享内容保留。

`repair V0.2.0` 从固定组件修复；断网时可用 `repair V0.2.0 --components <组件归档目录>`，仍逐项校验原始组件及内容哈希。共享对象损坏时会列入所有受影响版本，要求它们停止后再用已验证的新文件替换，不原位修改共享内容。旧完整包和外部登记请使用原始包重新解压。占用、未知文件或部分失败都会报告错误，保留登记及恢复信息，可先运行 `recover` 再修复；`collect` 单独检查并回收无引用内容。

## 工程作者版本绑定

「打开作者工程」选择目录后可查看实际版本和来源。解析顺序是本次显式选择、工程 `author.engine.json` 绑定、用户默认；缺失或损坏会提示安装／修复指定版本，不静默改用其他版本。临时选择不会改写绑定，「保存工程绑定」和「清除工程绑定」是两个独立动作。绑定文件仅包含 `{"api":1,"version":"0.2.0"}`，与 `author.profile.json` 插件配置和 `game.start.json` 运行入口分离，适用于两种作者格式。

```powershell
./bgb-launcher.exe binding --project 'D:/游戏 工程' --set V0.2.0
./bgb-launcher.exe resolve --project 'D:/游戏 工程'
./bgb-launcher.exe open --project 'D:/游戏 工程'
./bgb-launcher.exe open --project 'D:/游戏 工程' --version V0.2.1
./bgb-launcher.exe binding --project 'D:/游戏 工程' --clear
```

未传工程位置时，查询会从当前目录向上寻找入口或绑定文件。新建工程也可显式指定尚未存在的目录；保存绑定要求目录已存在。启动器只把目录交给对应 Editor，兼容检查和显式迁移仍属于该版作者工具；绑定不会修改游戏规则、定义或运行格式，导出的游戏不需要启动器或此作者配置。

## 统一 CLI 与按版本作者 SKILL

`cli [--version <版本>] [--project <目录>] -- <原 CLI 参数>` 使用同一选择顺序；工程可放在路由选项或原 CLI 参数中，两处不一致时拒绝。`--` 后的参数、JSON 和退出状态保留，Ctrl+C 返回 130。有效版本查询和固定多步创作示例：

```powershell
$version = (./bgb-launcher.exe resolve --project 'D:/游戏 工程' | ConvertFrom-Json).version
./bgb-launcher.exe cli --version $version --project 'D:/游戏 工程' -- operations
./bgb-launcher.exe cli --version $version --project 'D:/游戏 工程' -- describe project.validate
./bgb-launcher.exe cli --version $version -- docs entry
./bgb-launcher.exe cli --version $version --project 'D:/游戏 工程' -- call project.validate
```

GUI profile 仍由 `author.profile.json` 选择 `edit.gui`。路由的 `workspaces` 查询返回同版实例及工作区／文档代次；多步调用保留 `--version`、`--project` 和 `--instance`。CLI 会检查公告的进程启动时间与实际安装位置，跨版本或无法确认的 Editor 会被拒绝，不回退到磁盘模式。

```powershell
./bgb-launcher.exe skill install --version V0.2.0 --target 'D:/Agent/skills'
./bgb-launcher.exe skill install --version V0.2.1 --target 'D:/Agent/skills' --method junction
./bgb-launcher.exe skill status --version V0.2.0 --target 'D:/Agent/skills'
./bgb-launcher.exe skill repair --version V0.2.0 --target 'D:/Agent/skills'
./bgb-launcher.exe skill uninstall --version V0.2.0 --target 'D:/Agent/skills'
```

启动器技能分别安装为 `bgb-author-v0-2-0`、`bgb-author-v0-2-1`，模板和本地文档取自对应完整发行包，命令固定启动器路径、设置目录及显式版本。两版维护互不覆盖，也不覆盖直接单版本 CLI 的 `bgb-author` 技能。更改默认或安装新版不改变已有技能；删除引擎后调用会明确失败。手动更新启动器后，可从新启动器对同一版本执行 repair 更新路径，原固定路由不变。卸载只处理已登记归属的文件，发现用户改写会停止并保留。

## 设置与手动更新

共享内容和版本目录必须在同一个本地 NTFS 固定磁盘的存储根目录内；网络盘及不支持硬链接的位置会被拒绝。内容按哈希保存，相同字节只占一份物理数据，文件管理器显示的多个版本文件大小会重复计算硬链接。不要直接修改共享对象或产品文件；校验失败时安装会明确停止。

设置默认保存在当前用户的 `%LOCALAPPDATA%/BoardGameBuilder/Launcher`，包含安装位置、清单身份、运行时模式和默认版本。产品目录中的 `.state` 是该版本的可写运行缓存，不属于共享产品文件。共享存储位置单独记录，不能在已有安装时直接切换。

手动更新时关闭启动器，将新启动器解压到新目录，再运行其中的入口；保留上述用户设置目录。无需搬动引擎或工程。需要独立配置时，GUI 和 CLI 均支持 `--home <设置目录>`，两者应使用相同位置。设置损坏时会报告错误，不会静默清空登记。

开发者可运行 `pwsh -NoProfile -File scripts/publish-launcher.ps1` 生成独立 Windows x64 启动器，输出目录必须不存在。启动器自身版本独立于引擎产品版本；首期手动更新。
