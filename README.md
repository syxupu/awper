# AWPER

AWPER 是一个用于 **Counter-Strike 2 专用服务器**的 AWP 横拉训练插件。管理员可以直接在官方地图中记录玩家站位、Bot 起点和终点，保存为可复用的训练轨迹；玩家加载轨迹后，通过固定摄像机确认视角，并反复练习 Bot 的两点横向移动。

当前版本：**1.1.0**

> 本项目是社区插件，与 Valve 无隶属或背书关系。它不是创意工坊地图，必须运行在安装了 CounterStrikeSharp 的 CS2 专用服务器上。

## 主要功能

- 在官方地图内创建、验证、保存和加载训练轨迹。
- 通过 `!edit <名称>` 进入独立编辑模式，避免普通玩家误修改点位。
- 记录 `EditAnchor`、`PlayerAnchor`、`BotStart`、`BotEnd` 和可选的 `BotJiggle`。
- Mouse4 单击切换固定摄像机预览；摄像机位于 `PlayerAnchor`，玩家模型保持按键瞬间的朝向。
- Bot 使用真实 Pawn 与 CS2-Bot-Controller 原生输入回放移动，不使用逐 Tick Teleport。
- Bot 依据服务器实时 `sv_accelerate`、`sv_friction`、`sv_stopspeed` 和 TickInterval 从静止加速。
- 支持单次指定速度启动，不修改轨迹本身：`!start_speed <1-215>`。
- 支持复制轨迹并只修改名称和速度：`!copy <原名称> <新名称> <1-215>`。
- F5 打开 CounterStrikeSharp 原生中央菜单；聊天指令始终可以作为备用入口。
- 保存、加载和启动前执行站立空间、地面、路径扫掠和视线检查，验证失败时拒绝运行。
- 训练结束后幂等清理 Bot 与掉落武器，支持连续轮次。

## 运行逻辑

```text
管理员创建轨迹
  !edit <名称>
        │
        ├─ EditAnchor       编辑入口
        ├─ PlayerAnchor     玩家训练位置 / 摄像机位置
        ├─ BotStart         Bot 起点
        ├─ BotEnd           Bot 终点
        └─ BotJiggle        可选急停点
        │
        ▼
  实时 Ray-Trace 验证 ──失败──> 拒绝保存并说明原因
        │通过
        ▼
  保存为 profiles/<地图>/<名称>.json

玩家训练
  !load <名称> → Mouse4 预览 → Mouse5 / !start
        │
        ▼
  倒计时 → 随机延迟 → 创建 Bot → 原生输入回放移动
        │
        ├─ Bot 死亡
        ├─ 到达终点
        ├─ 卡住 / 超时
        └─ 玩家中止
        │
        ▼
  清理 Bot 和武器 → 恢复玩家 → 可以开始下一轮
```

更完整的命令参数与编辑顺序见 [command.md](command.md)。

## 运行要求

- CS2 专用服务器。
- [Metamod:Source](https://www.sourcemm.net/)。
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) API 1.0.373 或更高。
- [CS2-Bot-Controller](https://github.com/XBribo/CS2-Bot-Controller)，当前接口 ABI 19。
- [FUNPLAY Ray-Trace](https://github.com/FUNPLAY-pro-CS2/Ray-Trace)，包括 native、RayTraceImpl 和共享 RayTraceApi。
- 目标 CS2 版本需要支持 `cs_player_camera`。

发布包不会重复分发上述依赖的二进制文件，请按各上游项目的说明分别安装。

## 安装

1. 从 GitHub Releases 下载 `AwperTrainer-1.1.0.zip` 并解压。
2. 将以下运行文件放入服务器：

```text
game/csgo/addons/counterstrikesharp/plugins/AwperTrainer/
├─ AwperTrainer.dll
├─ AwperTrainer.Core.dll
├─ AwperTrainer.deps.json
└─ AwperTrainer.runtimeconfig.json
```

3. 将发布包中的桥接资源复制到对应服务器目录：

```text
resources/awper_camera.vjs_c  -> game/csgo/scripts/awper/awper_camera.vjs_c
resources/awper_hud.vjs_c     -> game/csgo/scripts/awper/awper_hud.vjs_c
resources/awper_hud.vxml_c    -> game/csgo/panorama/layout/custom_game/awper_hud.vxml_c
resources/awper_hud.vcss_c    -> game/csgo/panorama/styles/custom_game/awper_hud.vcss_c
```

4. 确认 BotController 和 RayTrace 的插件、共享 API 与原生模块已经安装，然后重启服务器。
5. 进入服务器后输入 `!status`，确认 BotController、RayTrace 和 camera 均可用。

插件配置位于：

```text
game/csgo/addons/counterstrikesharp/configs/plugins/AwperTrainer/AwperTrainer.json
```

训练轨迹按地图分别保存：

```text
game/csgo/addons/counterstrikesharp/configs/plugins/AwperTrainer/profiles/<map>/<name>.json
```

## 玩家按键

客户端控制台可以直接设置：

```cfg
bind "F5" "css_ui"
bind "MOUSE4" "css_preview_toggle"
bind "MOUSE5" "css_start"
```

也可以把发布包中的 `awper_bindings.cfg` 放入客户端 `game/csgo/cfg/`，然后执行：

```text
exec awper_bindings
```

F5 使用的是服务器提供的 CounterStrikeSharp 中央菜单。即使没有 F5 绑定，也可以在聊天框输入 `!ui` 打开菜单。

## 快速开始

加载已有轨迹：

```text
!list
!load mirage_awp_1
```

加载后先按 Mouse4 检查摄像机，再按 Mouse5 开始训练。也可以使用聊天命令：

```text
!preview_toggle
!start
```

以 180 units/s 只启动本轮，不修改原轨迹：

```text
!start_speed 180
```

## 创建轨迹

```text
!edit mirage_awp_1
!set_edit_anchor
!set_player_anchor
!set_bot_start
!set_bot_end
!set_bot_facing
!mode 1
!speed 215
!validate
!save
```

建议顺序：

1. 在安全位置进入编辑模式并记录 `EditAnchor`。
2. 走到实际训练位置，记录 `PlayerAnchor`。
3. 走到 Bot 出现位置，记录 `BotStart`。
4. 走到横拉结束位置，记录 `BotEnd`。
5. 根据需要记录 Bot 面向与急停点。
6. 使用 Mouse4 预览，再执行 `!validate` 和 `!save`。

轨迹名称只允许英文字母、数字、下划线和连字符，长度为 1–64 个字符。

## 常用命令

| 指令 | 作用 |
|---|---|
| `!help` | 显示简要帮助 |
| `!ui` | 打开或关闭 F5 中央菜单 |
| `!status` | 查看插件、依赖和当前训练状态 |
| `!list` | 列出当前地图的轨迹 |
| `!load <名称>` | 加载轨迹 |
| `!start` | 使用轨迹保存的速度开始 |
| `!start_speed <1-215>` | 只为本轮覆盖 Bot 速度 |
| `!copy <原名称> <新名称> <1-215>` | 复制轨迹，只改变名称和速度 |
| `!abort` | 中止训练或编辑并恢复玩家 |
| `!maps` | 查看允许切换的地图 |

所有聊天命令的 `!` 均可换为 `/`；控制台形式是在名称前加 `css_`，例如 `!start` 对应 `css_start`。AWPER 指令不再使用旧的 `awper_` 前缀。

## Bot 移动模型

配置中的速度是目标地速上限。每 Tick 先计算地面摩擦，再按 Source 地面加速模型沿路径方向增加速度，因此 Bot 不会在第一帧瞬间达到 215 units/s。较短的路径可能在达到目标速度前已经结束，这是正常结果。

插件不会通过逐 Tick Teleport 模拟移动；如果 BotController 接口或原生能力不可用，训练将直接拒绝启动。

## 构建与测试

需要 PowerShell 7 和 `.NET SDK 10.0.201`：

```powershell
pwsh.exe -NoLogo -NoProfile -NonInteractive -File .\build.ps1
```

该命令执行 Release 构建、自动化测试、格式检查、PowerShell 语法检查，并生成：

```text
artifacts/AwperTrainer.zip
```

## 项目结构

```text
src/AwperTrainer.Core/       轨迹、验证策略和训练状态机
src/AwperTrainer.Plugin/     CounterStrikeSharp 插件与游戏实体控制
tests/                       自动化测试
assets/                      摄像机与 HUD 桥接资源
config/                      示例配置和按键绑定
tools/                       部署与安装验证工具
command.md                   完整中文指令手册
```

## 已知边界

- 仓库只提供插件逻辑，不附带官方地图内容或预制七图轨迹。
- 每张地图、每个训练位置仍需要管理员自行记录并验证轨迹。
- CS2、CounterStrikeSharp、BotController 或 RayTrace 更新后，应重新执行实机验证。
- 当前设计一次只运行一个训练会话，适合个人或轮流训练服务器。

## 许可

本项目使用 [AGPL-3.0](LICENSE) 发布，以兼容 CS2-Bot-Controller 的 AGPL-3.0 依赖路径。第三方组件和商标说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
