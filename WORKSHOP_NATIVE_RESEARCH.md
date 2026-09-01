# CS2 AWPer Trainer 创意工坊原生替代方案调研

## 1. 调研结论

截至 2026-08-28，CS2 已提供官方地图脚本系统 `cs_script`。因此，当前 AWPer Trainer 的大部分服务端插件功能可以改写为随创意工坊地图发布的原生地图逻辑。

最接近完整替代插件的技术路线不是把 CounterStrikeSharp、Metamod 或 C# DLL 放入地图，而是重新实现为：

```text
Workshop Map
├── point_script / cs_script JavaScript
├── custom_hud_layout（XML/CSS）
├── 内嵌 Bot Behavior Tree（KV3）
├── 动态目标和朝向标记实体
├── 地图 NavMesh
└── 训练场景与点位
```

推荐的 Bot 运动后端是：

> `cs_script` 动态移动目标标记实体，Bot Behavior Tree 使用 `action_move_to` 驱动真实 Bot 原生移动。

该方案有望保留真实 Bot 的原生寻路、加速、摩擦、台阶、碰撞、移动动画和命中箱更新，同时不再依赖 CS2-Bot-Controller 的 usercmd 注入能力。

如果只评价“订阅地图后即可进行 AWP 横拉训练”的产品体验，预计可替代现有插件约 90%–95%。如果要求继续在七张未经修改的官方竞技地图上运行，则纯创意工坊方案无法完全替代服务端插件。

## 2. 当前版本核实

本次调研读取了本机安装的 CS2 Workshop Tools 官方类型定义：

```text
E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\content\csgo\maps\editor\zoo\scripts\point_script.d.ts
```

核实信息：

- CS2 build：`24957633`
- `point_script.d.ts` 修改时间：`2026-08-26 08:21:59 +08:00`
- 文件大小：`41520` 字节
- SHA-256：`0FDB1857860264B09A5361436A1EACBC69F03492F178BD4028ED4C0815023E2F`
- Valve 示例地图：`content\csgo\maps\editor\zoo\script_zoo.vmap`

这说明以下调研结论基于当前本机实际安装的 API，而不是仅基于旧版 CS:GO VScript 资料。

## 3. `cs_script` 可以覆盖的插件能力

### 3.1 训练状态机与计时

可使用：

- `SetThink`
- `SetNextThink`
- `Delay`
- `QueueAfterThinks`
- 玩家连接、重生、伤害、死亡、开枪和回合事件回调

可以实现：

- 三秒倒计时。
- 0.5–3 秒随机延迟。
- Direct Peek 和 Jiggle Peek 状态机。
- Bot 到达、死亡、卡住和超时处理。
- generation/session id 防止旧回调串轮。
- 中止、重置、换图和断线清理。

### 3.2 玩家输入

`CSPlayerPawn` 提供：

- `IsInputPressed`
- `WasInputJustPressed`
- `WasInputJustReleased`

目前可读取的官方输入包括：

- 前、后、左、右
- 静步、蹲下、跳跃、使用
- 主攻击、副攻击、换弹
- 记分板和检视武器

限制：API 没有直接暴露 Mouse4/Mouse5。因此创意工坊版应优先提供 HUD 按钮或使用键交互；侧键只能作为用户自行绑定的可选入口。

### 3.3 摄像机预览

可使用：

- `CSPlayerPawn.GetCamera()`
- `CSPlayerCamera.SetEnabled()`
- `CSPlayerCamera.SetIsControllingAngles()`
- 从 `Entity` 继承的 `Move()`、`Teleport()` 和角度接口

可实现：

- PlayerAnchor 固定视角预览。
- 编辑状态和预览状态切换。
- 预览异常退出后的摄像机强制关闭。
- 不传送玩家 Pawn 的摄像机预览。

### 3.4 HUD 和交互界面

`custom_hud_layout` 支持：

- Panorama XML/CSS。
- `Panel`、`Label`、`Image`、`Button`。
- `OnCustomHudClicked`。
- 玩家级输入捕获。
- 按玩家设置 CSS class 和对话变量。

Valve 示例资源位于：

```text
content\csgo\maps\editor\zoo\scripts\welcome.xml
content\csgo\maps\editor\zoo\scripts\welcome.css
```

建议用 HUD 替代大部分 `css_awper_*` 控制台命令，包括：

- 编辑点位。
- 切换 Direct/Jiggle 模式。
- 保存、加载和删除配置。
- 开始、中止训练。
- 当前状态、倒计时和错误提示。

### 3.5 几何验证

API 提供：

- `TraceLine`
- `TraceSphere`
- `TraceBox`
- `TracePlayer`
- `TraceBullet`

可以重写现有插件的：

- PlayerAnchor、BotStart、BotEnd 和 BotJiggle 空间检查。
- 玩家碰撞盒路径扫描。
- 地面和落脚点验证。
- LOS 检查。
- 起点到终点的路径走廊检查。

### 3.6 配置持久化

可使用：

- `SetSaveData(string)`
- `GetSaveData()`

SaveData 与 Workshop addon 关联，可以保存序列化 JSON，例如：

```json
{
  "schemaVersion": 1,
  "profiles": {
    "mirage_awp_1": {
      "playerAnchor": {},
      "botStart": {},
      "botEnd": {},
      "botJiggle": {},
      "botFacingYaw": 0
    }
  }
}
```

注意：`SetSaveData` 每次调用都会同步写盘，不能在 Tick 中持续调用。应只在玩家明确保存、删除或迁移配置时写入。

### 3.7 Bot 和武器

API 和 Valve 示例已经证明可以：

- 使用 `ServerCommand("bot_add")` 创建 Bot。
- 使用 `IsBot()` 识别 Bot 控制器。
- 查找 Bot Pawn。
- 移动、传送、伤害、击杀和移除实体。
- 修改生命、护甲和武器。
- 监听 Bot 受伤和死亡。

创意工坊版不需要强制更换玩家武器。正式实现中应避免对训练玩家调用：

- `DestroyWeapons()`
- `GiveNamedItem()`
- `SwitchToWeapon()`

可以保留正常购买区、武器墙或玩家当前武器。

## 4. 推荐 Bot 运动方案：动态 Behavior Tree

### 4.1 为什么优先使用 Behavior Tree

当前 `point_script.d.ts` 只提供输入读取，没有提供：

- 向 Bot 写入 `CSInputs`。
- 注入 Bot usercmd。
- 设置 Bot analog forward/side move。
- 调用 CS2-Bot-Controller 的移动 token。

但当前 CS2 仍保留官方 Bot Behavior Tree 系统：

- `mp_bot_ai_bt <path>`：设置 Bot 使用的行为树。
- `mp_bot_ai_bt_clear_cache`：清理行为树缓存。
- KV3 节点 `action_move_to`：让 Bot 原生移动到坐标或实体。
- KV3 节点 `action_look_at`：控制 Bot 朝向。

社区当前的 CS2 Behavior Tree 项目说明，地图专用行为树可以嵌入地图 VPK，并通过 `mp_bot_ai_bt` 加载。

### 4.2 动态目标实体桥接

行为树的 `action_move_to.destination` 不必是写死的坐标。它可以引用 `decorator_sensor` 找到的实体。

概念示例：

```kv3
{
    type = "decorator_sensor"
    shape =
    {
        type = "sensor_shape_sphere"
        radius = 20000
    }
    entity_type_filter = "CLASSNAME"
    class_name = "info_target"
    orphan_only = 1
    output = "AwperMovementTarget"
    child =
    {
        type = "action_move_to"
        destination = "AwperMovementTarget"
        movement_type = "BT_ACTION_MOVETO_RUN"
        route_type = "BT_ACTION_MOVETO_FASTEST_ROUTE"
        auto_look_adjust = 0
        arrival_epsilon = 4
    }
}
```

`point_script` 维护一个不可见、无碰撞的目标实体，并将它移动到当前训练状态的目标点：

```text
Prepare       → MovementTarget = BotStart
DirectPeek    → MovementTarget = BotEnd
JiggleOut     → MovementTarget = BotJiggle
JiggleReturn  → MovementTarget = BotStart
FinalPeek     → MovementTarget = BotEnd
Finish        → MovementTarget = Bot 当前位置或移除 Bot
```

这样，运行时保存的任意坐标可以通过实体位置传给静态 KV3 行为树，不需要在运行时生成或写入 KV3 文件。

### 4.3 固定 Bot 朝向

可设置第二个不可见实体作为 FacingTarget，放在 PlayerAnchor 的视线方向上。

行为树使用：

```kv3
type = "action_look_at"
input_location = "AwperFacingTarget"
```

与 `action_move_to` 并行或每个行为树 Tick 重复执行，同时把：

```kv3
auto_look_adjust = 0
```

设置在移动节点上。

目标效果是：Bot 朝向 PlayerAnchor，但沿 BotStart、BotJiggle 和 BotEnd 之间横向移动。

### 4.4 Bot 生命周期

推荐顺序：

1. 地图激活后设置 `mp_bot_ai_bt`。
2. 必要时执行 `mp_bot_ai_bt_clear_cache`。
3. 清理上一轮受控 Bot。
4. 添加且只保留一个训练 Bot。
5. 确认控制器 `IsBot()`。
6. Bot 重生后取得 Pawn。
7. 传送 Bot 到 BotStart；不传送玩家。
8. MovementTarget 留在 BotStart，完成倒计时和随机延迟。
9. 移动目标实体启动训练。
10. Bot 到达或死亡后停止、移除并回到 IdleReady。

自定义行为树中不应加入攻击分支。还可附加：

```text
bot_dont_shoot 1
bot_ignore_enemies 1
```

这些命令需要在只用于训练的本地地图环境中谨慎使用。

### 4.5 需要实机验证的 Behavior Tree 风险

- Workshop VPK 是否正确包含 `scripts/ai/awper/*.kv3`。
- `mp_bot_ai_bt` 从 Workshop VPK 加载文件的实际路径。
- Bot 是否必须重生或重开回合才能加载新树。
- 动态移动目标实体后，`action_move_to` 是否每 Tick 更新目的地。
- `auto_look_adjust = 0` 和持续 `action_look_at` 能否稳定保持横向朝向。
- 快速移动 MovementTarget 进行反向 Jiggle 时，Bot 是否自然急停并反向。
- arrival epsilon、NavMesh 路径、动态障碍物和台阶对端点判定的影响。

## 5. 后备 Bot 运动方案

### 5.1 `bot_strafe`

当前 CS2 仍保留：

```text
bot_strafe <interval>
```

当前描述为：

```text
Strafe left and right (interval)
```

公开 CS2 基准脚本同时使用：

```text
bot_stop 1
bot_strafe 0.6
bot_strafe 0
```

这说明在普通 Bot AI 停止时，`bot_strafe` 仍可驱动 Bot 侧移。它能保留真实 Bot 的移动表现，适合作为 Behavior Tree 不可用时的后备方案。

限制：

- 作用于全部 Bot，而不是单独一个 Bot。
- 主要按时间间隔切换方向。
- 没有直接暴露当前侧移方向。
- 精确 BotStart/BotJiggle/BotEnd 控制需要端点检测和方向校准。

由于当前产品只需要一个训练 Bot，全局作用仍可接受，但确定性弱于 Behavior Tree 动态目标方案。

### 5.2 `Entity.Move({ velocity })`

`Entity.Move()` 的官方说明是：

> Move this entity without resetting the client's interpolation history.

可按 Tick 修改真实 Bot Pawn 的位置、角度和速度，但仍需实机验证：

- 玩家移动物理是否会覆盖设置的速度。
- 模型动画和命中箱是否自然。
- 台阶、摩擦和碰撞是否由游戏继续处理。
- 网络插值和服务器位置是否一致。

因此建议只把它用于：

- Marker 和摄像机移动。
- 幽灵或展示实体。
- 端点纠偏实验。
- Behavior Tree 和 `bot_strafe` 都失败时的技术原型。

不建议把逐 Tick `Teleport()` 作为正式 Bot 运动后端。

## 6. 功能替代度

| 现有插件能力 | Workshop 原生方案 | 结论 |
|---|---|---|
| PlayerAnchor 摄像机预览 | `GetCamera()` + `SetEnabled()` | 可完整替代 |
| EditAnchor 和 Bot 点位编辑 | 玩家位置/角度 + HUD | 可完整替代 |
| 半透明幽灵 | `PointTemplate` 生成模型实体 | 可替代 |
| LOS 和路径验证 | Trace API | 可完整替代 |
| 配置保存、加载、列出和删除 | `SetSaveData`/`GetSaveData` | 可替代 |
| 倒计时和随机延迟 | Think/Delay | 可完整替代 |
| 单个训练 Bot 生命周期 | `bot_add` + `IsBot()` + Pawn API | 可替代 |
| Direct Peek | Behavior Tree 动态目标 | 高度接近，需实测 |
| Jiggle Peek | 动态切换目标实体 | 高度接近，需实测 |
| Bot 固定面向 PlayerAnchor | `action_look_at` | 高度接近，需实测 |
| Bot 原生物理、动画和命中箱 | `action_move_to` | 比 `Move()` 更接近插件 |
| 不传送训练玩家 | 状态机只控制 Bot | 可完整做到 |
| 不强制更换玩家武器 | 不调用武器修改接口 | 可完整做到 |
| Mouse4/Mouse5 原生输入 | API 未直接暴露 | 需用户绑定或改用 HUD |
| 在原版官方地图上直接运行 | 地图中没有 `point_script` | 纯 Workshop 无法做到 |
| 任意服务器安装即用 | 无需 native plugin，但必须加载该 Workshop 地图 | 部分替代 |

## 7. 发布形态

### 7.1 推荐：独立 AWPer Trainer 地图

制作一个完整的训练地图，包含若干复刻或抽象化的常用 AWP 对枪场景：

- A 大身位横拉。
- 小身位 Jiggle。
- 中距离拐角。
- 高低差和箱体边缘。
- 不同可见前加速距离。

优点：

- 用户订阅后即可运行。
- 不需要服务器插件和原生依赖。
- 可以完全控制 NavMesh、训练场景和实体布局。
- 最容易满足创意工坊发布和更新流程。
- 不需要复制完整官方地图。

预计产品体验替代度：90%–95%。

### 7.2 每张地图制作增强版

例如：

```text
awper_mirage
awper_inferno
awper_nuke
...
```

优点：训练场景更接近比赛点位。

风险：

- 官方地图完整 Hammer 源文件并未随本机 Workshop Tools 提供。
- 复制、反编译或重新发布官方地图存在内容授权风险。
- 官方地图更新后需要重新维护几何、材质、NavMesh 和训练配置。
- 地图体积和 Workshop 更新成本较高。

在未明确内容授权前，不建议把复制七张官方地图作为首选产品路线。

### 7.3 继续保留服务端插件

如果必须满足以下条件：

> 在七张未经修改的官方竞技地图中动态记录任意训练点。

那么服务端插件仍不可替代。纯 Workshop 地图无法把 `point_script` 注入当前加载的官方地图。

可考虑维护两个版本：

- Workshop 独立训练地图：面向普通用户，订阅即用。
- 服务端插件版：面向社区服务器和七图自由编辑需求。

## 8. 推荐原型计划

### P0：最小 Movement Spike

只制作一个直线训练房：

1. 放置 `point_script`。
2. 放置 MovementTarget 和 FacingTarget。
3. 内嵌最小 Behavior Tree。
4. 创建一个真实 Bot。
5. BotStart 到 BotEnd 原生横向移动。
6. Bot 全程面向固定 FacingTarget。
7. 玩家不被传送、不被换武器。

通过条件：

- Bot 模型可见。
- 移动动画自然。
- 玩家碰撞和世界碰撞正常。
- AWP 命中、伤害和死亡正常。
- 服务器位置和客户端观察位置无明显漂移。

### P1：动态状态机

增加：

- 三秒倒计时。
- 随机延迟。
- Direct 模式。
- BotStart ↔ BotJiggle 1–4 次。
- Bot 死亡、到达、卡住和超时清理。

通过条件：Direct 和 Jiggle 各连续运行 100 轮，无重复 Bot 和旧状态串轮。

### P2：编辑、摄像机和存档

增加：

- HUD 编辑工作流。
- PlayerAnchor、BotStart、BotEnd、BotJiggle 和 BotFacing。
- PlayerAnchor 摄像机预览。
- Trace 验证。
- SaveData JSON 持久化。

通过条件：用户不编辑地图或配置文件即可创建、保存、加载并运行配置。

### P3：隐藏 Workshop 发布验证

发布为隐藏或仅好友可见的 Workshop item，在一台没有 Metamod、CounterStrikeSharp 和相关插件的干净 CS2 环境验证：

- 订阅后地图可启动。
- `.vjs_c` 正常加载。
- HUD XML/CSS 正常加载。
- Behavior Tree KV3 能从地图 VPK 读取。
- SaveData 重启后仍保留。
- 不依赖本机开发目录中的松散文件。

## 9. 关键验收测试

### 9.1 Bot 运动质量

- 每 Tick 记录位置、速度、目标距离和状态。
- 验证满速横拉和可见前预加速。
- 验证快速反向时加速、摩擦和急停。
- 验证模型动画、命中箱和服务器位置一致。
- 验证 30、60、100 ms 延迟下的观察效果。

### 9.2 状态清理

- Bot 死亡只结束一次。
- Bot 到达只结束一次。
- 玩家死亡、断线、换队和回合重置。
- 地图退出和脚本异常。
- 无残留 Bot、摄像机、HUD 输入捕获和旧 Think 回调。

### 9.3 玩家体验

- 点击开始不传送玩家。
- 结束训练不传送玩家。
- 不修改当前武器。
- 不把玩家原生移动速度设置为零。
- 打开 HUD 时才捕获输入，关闭后立即恢复移动。

## 10. 风险和未决问题

1. `cs_script` 和 `custom_hud_layout` 当前仍标记为实验性功能，CS2 更新可能造成 API 变更。
2. Behavior Tree 虽仍存在于当前 CS2，但需要完成 Workshop VPK 内嵌和加载验证。
3. `action_move_to` 是否能在固定朝向时稳定形成训练所需的横拉，需要实机测试。
4. 动态 Marker 更新、快速反向和端点停顿需要实测行为树 Tick 语义。
5. `mp_bot_ai_bt` 对所有重生 Bot 生效，因此 Workshop 训练地图应只保留一个受控 Bot。
6. Mouse4/Mouse5 不能通过 `CSInputs` 直接检测，需要 HUD、标准按键或用户自行绑定。
7. SaveData 是 addon 级字符串存储，需设计 schema、容量控制和迁移策略。
8. 纯 Workshop 版本无法在原版 `de_mirage` 等地图中注入脚本。
9. 不应直接复制没有明确许可证的社区 Behavior Tree 文件；应基于公开节点格式自行编写最小树，或取得作者许可。

## 11. 最终建议

推荐按以下优先级继续：

1. 先验证 `point_script + 动态 Marker + Behavior Tree action_move_to`。
2. Behavior Tree 成功后，用它作为正式 Bot 运动后端。
3. `bot_strafe` 作为单 Bot 后备方案。
4. `Entity.Move({ velocity })` 只作为实验和纠偏手段。
5. 产品形态优先选择独立训练地图，而不是复制七张官方地图。
6. 保留现有服务器插件版，服务需要在原版官方地图上自由编辑训练点的高级用户。

结论：目前最可能实现“无需安装服务器插件、订阅创意工坊即可运行、真实 Bot 原生移动”的方案，是：

> `Workshop Map + cs_script + custom_hud_layout + SetSaveData + 动态标记实体 + 内嵌 Bot Behavior Tree`

## 12. 参考资料

- [Source2Wiki：cs_script 入门](https://www.source2.wiki/Scripting/Counter-Strike%202/cs_script/introduction)
- [Source2Wiki：cs_script API](https://www.source2.wiki/Scripting/Counter-Strike%202/cs_script/functionList)
- [Valve 当前 point_script 类型定义镜像](https://github.com/SteamTracking/GameTracking-CS2/blob/master/content/csgo/maps/editor/zoo/scripts/point_script.d.ts)
- [Valve Bot、HUD 与 ServerCommand 示例](https://github.com/SteamTracking/GameTracking-CS2/blob/master/content/csgo/maps/editor/zoo/scripts/setup.js)
- [Source2ZE：cs_script TypeScript boilerplate](https://github.com/Source2ZE/cs_script_boilerplate)
- [CS2 Behavior Tree 项目和格式说明](https://github.com/asquilatan/cs2-behavior-tree)
- [Behavior Tree 使用实体作为动态目的地的示例](https://github.com/asquilatan/cs2-behavior-tree/blob/main/ln/modules/bt_pickup_hostage.kv3)
- [现有纯 cs_script 瞄准训练地图源码](https://github.com/HiraiKyo/cs2-aim-js)
- [`bot_strafe` 当前说明](https://srprolin.top/commands/bot_strafe)
- [`bot_stop` 与 `bot_strafe` 同时使用的公开实例](https://github.com/AveYo/Gaming/blob/master/CS2/benchmark.bat#L399-L443)
