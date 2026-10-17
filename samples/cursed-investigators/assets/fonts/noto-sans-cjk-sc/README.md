# 随包中文字体

游戏动态文字使用 **Noto Sans CJK SC Regular 2.004**（静态 OpenType／CFF `.otf`），字体文件为 [NotoSansCJKsc-Regular.otf](NotoSansCJKsc-Regular.otf)。未安装到系统，也未修改或子集化字体；保留完整中文、拉丁文、数字与标点覆盖。

- 官方来源：[notofonts/noto-cjk](https://github.com/notofonts/noto-cjk)，固定 `Sans2.004` 版本及具体提交；准确下载 URL、文件大小、SHA-256 见 [source.json](source.json)。
- 许可证：[SIL Open Font License 1.1](LICENSE.txt)，导出游戏时随字体一并携带。
- 用途：当前操作提示、journal 动态记录、reference 的动态界面文字及其他游戏 UI。扫描卡牌上的原文字保留，不重绘。
- 已使用文件直接加载并检查规则／卡牌／入口文档内全部汉字的字形覆盖；这是资源校验，尚未证明引擎字体加载能力已完成。实际运行配置与干净环境验收属于后续游戏开发任务。
