# CS2 地图内前端（Custom HUD）实现方法

> 调研与实现基线：CS2 build `24957633`，2026-08-28。
> 本文只讨论可随创意工坊地图发布的官方方案：`custom_hud_layout + cs_script`。

## 1. 结论

CS2 已可在创意工坊地图或加载该地图的社区服务器中提供游戏内自定义前端，并按玩家显示不同状态、捕获鼠标、接收按钮点击。

推荐结构：

```text
Workshop Addon
├── custom_hud_layout                 # HUD 入口实体
├── Panorama XML                     # Panel / Label / Image / Button
├── Panorama CSS                     # 布局、颜色、显隐和 hover 状态
└── point_script / cs_script         # 状态、玩家隔离、按钮回调和游戏逻辑
```

当前官方能力：

- 支持 `Panel`、`Label`、`Image`、`Button`。
- 支持 CSS。
- 支持 `Instance.OnCustomHudClicked` 按钮回调。
- 支持逐玩家 CSS class 和对话变量。
- 支持 `SetInputCaptureEnabled(playerSlot, true)` 强制进入鼠标模式。
- 支持在关闭界面时恢复移动控制。

当前限制：

- 不支持 HUD 内客户端 JavaScript。
- 不支持在 XML 中注册任意客户端事件。
- 不支持直接读取物理键盘 Alt、Mouse4、Mouse5 等 raw key。
- `custom_hud_layout` 当前仍是实验性接口，CS2 更新后需要回归测试。
- `Image` 只能显示图片资源，不能作为任意 3D 摄像机的实时 RenderTarget。

## 2. 建议的资源结构

```text
content/csgo_addons/awper_trainer/
├── maps/
│   ├── awper_trainer.vmap
│   └── scripts/
│       └── awper_hud.js
└── panorama/
    ├── layout/custom_game/
    │   └── awper_hud.xml
    └── styles/custom_game/
        └── awper_hud.css
```

编译后，相应资源会进入 addon VPK。布局和样式的具体资源 URI 建议从 Hammer/Asset Browser 复制，避免手写编译资源路径出错。

## 3. Hammer 实体配置

在地图中加入两个实体：

### `custom_hud_layout`

- Entity Name：`awper_hud`
- Layout：选择 `awper_hud.xml` 对应的布局资源

### `point_script`

- Entity Name：`awper_ui_controller`
- cs_script：选择 `awper_hud.js` 对应的 `.vjs` 资源

名称必须与脚本中的 `Instance.FindEntityByName("awper_hud")` 一致。

## 4. 最小 XML 布局

`panorama/layout/custom_game/awper_hud.xml`：

```xml
<root>
    <styles>
        <include src="s2r://panorama/styles/custom_game/awper_hud.vcss_c" />
    </styles>

    <Panel class="AwperRoot" hittest="false">
        <!-- 常驻状态条不捕获鼠标 -->
        <Panel id="status_bar" class="StatusBar" hittest="false">
            <Label id="status_text" text="AWPer Trainer" />
        </Panel>

        <!-- 对话框通过 Hidden class 控制显隐 -->
        <Panel id="menu" class="Menu Hidden" hittest="true">
            <Label class="Title" text="AWPer Trainer" />

            <Button id="start_button" class="ActionButton">
                <Label text="开始训练" />
            </Button>

            <Button id="edit_button" class="ActionButton">
                <Label text="编辑点位" />
            </Button>

            <Button id="close_button" class="SecondaryButton">
                <Label text="关闭" />
            </Button>
        </Panel>
    </Panel>
</root>
```

说明：

- `Button` 的 `id` 会作为 `event.buttonId` 返回给服务器端 `cs_script`。
- `Panel` 的 `id` 用于 `SetHasClassForPlayer` 和 `SetDialogVariableStringForPlayer`。
- 常驻但不可操作的 HUD 应使用 `hittest="false"`。
- 菜单关闭时不要继续捕获输入。

## 5. 最小 CSS

`panorama/styles/custom_game/awper_hud.css`：

```css
.AwperRoot {
    width: 100%;
    height: 100%;
}

.StatusBar {
    horizontal-align: left;
    vertical-align: bottom;
    margin-left: 24px;
    margin-bottom: 180px;
    padding: 8px 12px;
    background-color: #10151ddd;
    border-radius: 4px;
}

.Menu {
    width: 420px;
    padding: 24px;
    flow-children: down;
    horizontal-align: center;
    vertical-align: center;
    background-color: #10151df2;
    border: 1px solid #58a6ff88;
    border-radius: 6px;
    opacity: 1;
}

.Menu.Hidden {
    opacity: 0;
    visibility: collapse;
}

.Title {
    horizontal-align: center;
    margin-bottom: 16px;
    font-size: 28px;
    color: white;
}

.ActionButton,
.SecondaryButton {
    width: 100%;
    height: 46px;
    margin-top: 8px;
    padding: 10px 14px;
    background-color: #253246;
}

.ActionButton:hover {
    background-color: #34547d;
}

.SecondaryButton:hover {
    background-color: #63363b;
}
```

应针对 16:9、16:10、4:3 和不同 HUD 缩放进行实际验证。

## 6. `cs_script` 控制逻辑

`maps/scripts/awper_hud.js`：

```js
import {
    CSInputs,
    CSPlayerPawn,
    Entity,
    Instance,
} from "cs_script/point_script";

let hud = null;

function getHud() {
    if (!(hud instanceof Entity) || !hud.IsValid()) {
        hud = Instance.FindEntityByName("awper_hud");
    }

    if (!(hud instanceof Entity) || !hud.IsValid()) {
        throw new Error("custom_hud_layout 'awper_hud' was not found");
    }

    return hud;
}

function setMenuOpen(playerSlot, open) {
    const layout = getHud();

    layout.SetHasClassForPlayer(
        playerSlot,
        "menu",
        "Hidden",
        !open,
    );

    layout.SetInputCaptureEnabled(playerSlot, open);
}

function toggleMenu(playerSlot) {
    const layout = getHud();
    const isOpen = layout.IsInputCaptureEnabled(playerSlot);
    setMenuOpen(playerSlot, !isOpen);
}

Instance.OnCustomHudClicked((event) => {
    if (event.layout !== getHud()) return;

    const playerSlot = event.player.GetPlayerSlot();

    switch (event.buttonId) {
        case "start_button":
            setMenuOpen(playerSlot, false);
            // startTraining(event.player);
            break;

        case "edit_button":
            setMenuOpen(playerSlot, false);
            // enterPointEditor(event.player);
            break;

        case "close_button":
            setMenuOpen(playerSlot, false);
            break;
    }
});

// 社区服插件可对 point_script 触发：
// RunScriptInput "ToggleMenu"，并把玩家 Pawn 作为 activator。
Instance.OnScriptInput("ToggleMenu", ({ activator }) => {
    if (!(activator instanceof CSPlayerPawn)) return;

    const controller = activator.GetPlayerController();
    if (!controller) return;

    toggleMenu(controller.GetPlayerSlot());
});

// 纯创意工坊方案：检测一个官方暴露的游戏动作。
function think() {
    for (const controller of Instance.GetAllPlayerControllers()) {
        if (!controller.IsConnected()) continue;

        const pawn = controller.GetPlayerPawn();
        if (!pawn) continue;

        if (pawn.WasInputJustPressed(CSInputs.LOOK_AT_WEAPON)) {
            toggleMenu(controller.GetPlayerSlot());
        }
    }

    Instance.SetNextThink(Instance.GetGameTime() + 0.01);
}

Instance.SetThink(think);
Instance.SetNextThink(Instance.GetGameTime() + 0.01);

// 所有离开编辑状态的路径都必须释放输入捕获。
Instance.OnPlayerReset(({ player }) => {
    const controller = player.GetOriginalPlayerController();
    setMenuOpen(controller.GetPlayerSlot(), false);
});

Instance.OnPlayerDisconnect(({ playerSlot }) => {
    setMenuOpen(playerSlot, false);
});
```

上面是结构示例。正式实现中应把 UI 状态放进按 `playerSlot` 索引的会话对象，避免多名玩家互相覆盖。

## 7. Alt 键实现方案

### 方案 A：纯创意工坊地图

`CSInputs` 没有暴露物理 Alt，因此地图不能知道玩家究竟按的是哪个键。可以让玩家主动把 Alt 绑定到地图可检测的游戏动作：

```cfg
bind alt +lookatweapon
```

地图检测 `CSInputs.LOOK_AT_WEAPON` 的按下沿并切换菜单。代价是 Alt 会同时成为检视武器键，且地图不能替玩家强制写入这个绑定。

也可以改用 `USE`、`SHOW_SCORES` 等动作，但应避免与训练操作冲突。

### 方案 B：社区服务器

服务器使用 CounterStrikeSharp 注册普通玩家命令：

```text
css_awper_ui
```

玩家主动绑定：

```cfg
bind alt css_awper_ui
```

命令回调拿到发起玩家后：

1. 找到地图中的 `point_script`：`awper_ui_controller`。
2. 对其触发 `RunScriptInput`，参数名为 `ToggleMenu`。
3. 将发起玩家的 Pawn 作为 `activator`。
4. 地图脚本通过 `Instance.OnScriptInput("ToggleMenu", ...)` 定位玩家并切换 HUD。

该桥接只依赖通用实体 I/O，不需要等待 CounterStrikeSharp 专门封装刚加入的 `custom_hud_layout` 类型。

### 建议采用切换式而非按住式

推荐行为：

- 按一次 Alt：打开菜单并捕获鼠标。
- 点击关闭按钮或再次按 Alt：关闭菜单并释放鼠标。

不建议依赖 `+awper_ui`/`-awper_ui` 的按住和松开事件。进入鼠标捕获后，按键释放事件可能无法按预期到达游戏逻辑。无论采用哪种交互，都必须保留可见的关闭按钮和异常清理路径。

## 8. 数据和显隐更新

### 按玩家切换 CSS class

```js
layout.SetHasClassForPlayer(
    playerSlot,
    "status_bar",
    "TrainingActive",
    true,
);
```

适合表示：

- 菜单开关。
- 训练进行中。
- 点位是否合法。
- 警告、成功和错误状态。

### 按玩家更新文本变量

```js
layout.SetDialogVariableStringForPlayer(
    playerSlot,
    "status_text",
    "mode",
    "Direct Peek",
);
```

适合显示：

- 当前训练模式。
- 倒计时。
- Bot 数量。
- 保存的点位名称。
- 几何验证和错误信息。

布局中的具体文本占位符语法应以本机 Valve `script_zoo` 示例和当前版本资源编译器为准。

## 9. 状态与清理约束

建议为每名玩家维护：

```text
PlayerUiSession
├── playerSlot
├── menuOpen
├── currentPage
├── inputCaptured
├── editingPoint
└── generation/sessionId
```

必须在以下情况释放输入：

- 关闭按钮。
- 开始训练。
- 中止编辑。
- 玩家死亡或重生。
- 玩家换队。
- 回合重置。
- 玩家断线。
- 脚本重载或地图清理。
- 任何异常退出路径。

注意：多个 `custom_hud_layout` 可以同时捕获同一玩家的输入。玩家只有在所有布局都关闭输入捕获后才会恢复移动控制，因此每个布局都必须独立、幂等地清理自身状态。

## 10. 测试清单

- [ ] 单人进入地图，HUD 能正常显示。
- [ ] 菜单关闭时不影响移动、开枪和转动视角。
- [ ] 打开菜单后出现鼠标，按钮可点击。
- [ ] 关闭按钮始终能释放输入捕获。
- [ ] 每个按钮只触发一次服务器逻辑。
- [ ] 两名玩家同时操作时状态不会串位。
- [ ] 玩家死亡、换队、断线后不会残留鼠标模式。
- [ ] 回合重置和工具模式热重载后状态可恢复或安全清空。
- [ ] 16:9、16:10、4:3 和不同 UI 缩放下布局可用。
- [ ] 创意工坊 VPK 中包含 XML、CSS 和脚本编译资源。
- [ ] 每次 CS2 更新后复测实验性 API。

## 11. 当前参考资料

- [Valve 2026-08-24 CS2 更新说明（简体中文）](https://steamcommunity.com/app/730/?curator_clanid=4759298&l=schinese)
- [当前 `point_script.d.ts`](https://github.com/SteamTracking/GameTracking-CS2/blob/master/content/csgo/maps/editor/zoo/scripts/point_script.d.ts)
- [Valve `custom_hud_layout` 官方示例脚本](https://github.com/SteamTracking/GameTracking-CS2/blob/master/content/csgo/maps/editor/zoo/scripts/setup.js)
- [Valve 示例 XML](https://github.com/SteamTracking/GameTracking-CS2/blob/master/game/csgo/pak01_dir/maps/editor/zoo/scripts/welcome.xml)
- [Valve 示例 CSS](https://github.com/SteamTracking/GameTracking-CS2/blob/master/game/csgo/pak01_dir/maps/editor/zoo/scripts/welcome.css)
- [CounterStrikeSharp 控制台命令文档](https://docs.cssharp.dev/docs/features/console-commands.html)
- [项目综合原生方案调研](../WORKSHOP_NATIVE_RESEARCH.md)

## 12. 推荐落地顺序

1. 先在 Hammer 中复现 Valve `welcome` 示例，确认资源能随地图加载。
2. 实现只有“打开/关闭”的单玩家 HUD。
3. 加入逐玩家状态和死亡/断线清理。
4. 接入 Alt 的纯地图绑定或社区服命令桥接。
5. 再接入训练模式、点位编辑和 Bot 管理。
6. 最后处理不同分辨率、错误提示和更新兼容性。
