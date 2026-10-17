# 动画库

通用内置运行插件 `resources.animations`（version `1`、api `1`）依赖 `resources`，配置为 `{"path":"animations.json"}`。只负责表现，不执行规则、随机或权威命令。作者操作由已有 `author.configuration` 插件贡献，CLI 使用原有的发现、自描述和调用协议。

```json
{
  "format": 1,
  "clips": [{
    "id": "move",
    "trigger": { "entity": "display", "property": "motionSerial" },
    "tracks": [{
      "entity": "token",
      "source": "supply",
      "keys": [
        { "timeMs": 100, "x": 0, "y": 0, "rotation": -10 },
        { "timeMs": 300, "x": 20, "y": -30, "rotation": 5 },
        { "timeMs": 600, "x": 0, "y": 0, "rotation": 0 }
      ]
    }]
  }]
}
```

`trigger` 引用投影可见的对象属性。已提交的正整数序号改变时播放，值相同的刷新不会重播；序号归零取消旧播放。新视口可播放初始正值，适合开局动画。隐藏触发对象不产生可见播放。游戏脚本只增加序号，动画不会替它执行移动或洗牌。

`entity` 为目标对象，`source` 可选，指定来源对象／区域。关键帧 x/y 为相对目标最终位置的像素偏移，rotation 为角度；可用正的首帧时间延迟轨道，组合先后顺序。存在 source 时，来源到目标的位移会随轨道进度衰减，额外关键帧偏移叠加其上。位置和旋转线性插值，末帧必须为零偏移和零旋转，落点始终来自当前投影。每轨道 2～64 帧、时间严格递增、最晚 10 秒；整个文件最多 128 个片段、2048 条轨道和 1 MiB。

可选 `backResource` 引用已登记图片，在播放期间代替目标图片，例如用统一背面表现移动；结束恢复权威投影图片。对象不在投影中时跳过轨道，不凭动画定义生成隐藏对象。配置本身是公开资源，不应包含隐藏身份或牌序。

```powershell
bgb.exe call animation.create --project <工程> --args '{"path":"animations.json"}'
bgb.exe call animation.read --project <工程>
bgb.exe describe animation.replace --project <工程>
bgb.exe call animation.replace --project <工程> --args-file <包含version和definition的JSON>
bgb.exe call animation.validate --project <工程>
bgb.exe validate --project <工程>
```

`animation.replace` 核对读取时的工程版本，经当前编辑模式提交。结构错误拒绝提交；缺少对象或图片等引用可保留为作者草稿，由共享校验定位，运行与导出会拒绝。创建不会覆盖文件或重复启用插件。

Editor 的内置 `author.animation` 插件提供“新建动画库”命令和“动画设计”面板。编辑共享 JSON 字段后显式应用，支持文档 Undo/Redo；显式保存才落盘。预览当前字段在临时资源快照中绘制，不运行游戏、不修改权威状态、不保存。无效字段和冲突保留；重建面板不丢草稿，停用插件或切换工程时释放预览及回调。独立 Player 不携带编辑器插件。
