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

## 设置与手动更新

共享内容和版本目录必须在同一个本地 NTFS 固定磁盘的存储根目录内；网络盘及不支持硬链接的位置会被拒绝。内容按哈希保存，相同字节只占一份物理数据，文件管理器显示的多个版本文件大小会重复计算硬链接。不要直接修改共享对象或产品文件；校验失败时安装会明确停止。

设置默认保存在当前用户的 `%LOCALAPPDATA%/BoardGameBuilder/Launcher`，包含安装位置、清单身份、运行时模式和默认版本。产品目录中的 `.state` 是该版本的可写运行缓存，不属于共享产品文件。共享存储位置单独记录，不能在已有安装时直接切换。

手动更新时关闭启动器，将新启动器解压到新目录，再运行其中的入口；保留上述用户设置目录。无需搬动引擎或工程。需要独立配置时，GUI 和 CLI 均支持 `--home <设置目录>`，两者应使用相同位置。设置损坏时会报告错误，不会静默清空登记。

开发者可运行 `pwsh -NoProfile -File scripts/publish-launcher.ps1` 生成独立 Windows x64 启动器，输出目录必须不存在。启动器自身版本独立于引擎产品版本；首期手动更新。
