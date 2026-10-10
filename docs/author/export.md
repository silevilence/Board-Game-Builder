# 游戏包与独立 Player

`project.pack` 接受新 target（.bgm 文件），通过正式 GamePackage 协议打包所选模式的固定视图。资源、脚本和定义从运行插件声明取得；作者 profile、SKILL 和 `.bgb-author` 不成为游戏运行依赖。

`player.export` 接受新 target（目录），可用 player 指定官方预编译 Player 目录；省略时使用 CLI 旁的 player 目录。预检核对 release.json、平台、文件哈希、所需运行插件、Lua worker 和正式图形入口。导出无需 SDK，不调用编译器。

成功目录包含 Play.exe、game.bgm 和运行依赖，双击 Play.exe 运行。首期为 Windows x64 独立目录，需要 Direct3D 11 图形环境；它不是单一文件发行。导出的游戏不依赖 CLI 或 Editor。

输出须在工程目录之外且原目标不存在；失败不会发布被报告为成功的最终产物。回执包含目标、产物种类、来源版本与游戏指纹。GUI 导出不隐式保存草稿。
