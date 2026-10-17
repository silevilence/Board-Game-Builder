# Player 标题与操作区

将 JSON 通过 `resources` 插件登记为二进制资源 `player.presentation`。现有 `title`、`help`、`showActions`、窗口尺寸和 `statusEntity` 可继续使用。

新增可选字段：

- `actionTray: true` 在桌面下方显示可滚动的合法动作区。可配合 `showActions: false` 默认隐藏右侧操作栏；查看菜单仍可切换右栏。
- `endActionLabel` 指定终局按钮文案；点击后以新种子创建新会话，释放旧会话与视图。终局前不显示，终局时仍可查看桌面与日志。
- `setup` 以公开投影中的 `entity`、`property`、`value` 判断是否显示分步开局。`steps` 按顺序定义 `title`、`text`、`nextLabel`、`actionKinds`，可选 `previewEntity` 和 `submitKind`。最后一页必须有 `submitKind`。

页面只筛选权威会话已经提供的合法动作，不凭配置创建命令；选项提交仍检查版本，按钮等待完成，终页提交指定种类的合法动作。预览仅读取当前席位投影的牌面资源，隐藏实体不会被提前展开。若配置实体属性名与命令种类相同、数值相等，按钮以勾号标明当前选项。

“返回／下一步”属于本地展示导航，不消耗回合、不改规则或回放；返回保留已提交的选择。权威状态离开配置绑定值后展示桌面。规则故障、终局与 CPU 行动时不显示开局页面。能力属于内置 Player 展示层，使用现有资源插件加载，无需新增 CLI 操作。当前分步页面与下方动作区适用于单席位 `PlayBoard`，本地热座／分屏不消费这两项配置。

`samples/cursed-investigators/presentation.json` 提供标题、模式、人物预览、职业预览及开始游戏的完整示例，相关内容均由 sample 定义。

## 牌桌内交互

`boardActions` 可替代侧栏及下方动作区。启用后 `PlayBoard` 不创建这两种操作区，查看菜单也不再将其打开；终局按钮放在 `endRegion`。

```json
{
  "boardActions": {
    "width": 1100, "height": 870,
    "regions": [
      {"kind": "select", "label": "", "anchor": "{entity}", "x": 0, "y": 0, "width": 0, "height": 0, "direct": true, "outline": true},
      {"kind": "ability", "label": "选择效果", "x": 40, "y": 320, "width": 100, "height": 40}
    ],
    "endRegion": {"x": 785, "y": 782, "width": 250, "height": 32}
  }
}
```

区域按 `kind` 匹配当前席位的合法动作，可再按 `value`、`property`、`target` 筛选；一项动作采用第一个匹配且可见的区域。`anchor` 可为可见实体 ID，或使用命令的 `{entity}`、`{value}`；坐标相对其牌面左上角，宽高为 0 时使用牌面尺寸。无锚点时坐标相对桌面，尺寸必须为正。画布范围须覆盖区域。

`outline` 绘制透明描边，保留扫描牌面；否则显示桌面按钮。`direct` 仅在恰好一个合法动作时直接提交；多个选项始终弹出分页菜单，`autoOpen` 可在进入奖励或选择阶段时自动打开。菜单每页六项，背景、关闭按钮和 Esc 关闭；翻页与取消不改变权威状态。每次提交后用新投影重建区域，不能沿用旧选项。隐藏实体没有点击区域；命令仍经 `PlayerSession` 当前版本及合法动作校验。

此配置属于通用内置 Player 呈现能力，由现有资源插件加载；游戏动作种类、区域、文案与终局文案均由模块提供。当前适用于单席位 `PlayBoard`，不改变本地热座／分屏行为，也不新增 CLI 命令。
