# CS2 AWP Bot 训练工具实施计划

## 1. 文档信息

- 项目代号：CS2 AWP Bot Trainer
- 文档状态：Draft v1
- 目标平台：CS2 社区服务器
- 地图范围：当前优先竞技地图池中的七张官方地图，通过配置白名单控制
- 首版使用范围：单服务器、单名训练用户、单个训练 Bot
- 首版不包含：Alt 交互前端、任意摄像机实时 PIP、多人并发训练、跳跃或梯子路径

## 2. 产品目标

在不修改七张官方地图的前提下，为玩家提供可保存、可重复执行的 AWP 对枪训练：

1. 玩家记录 Bot 编辑区域。
2. 玩家记录自己的训练位置和初始瞄准方向。
3. 玩家被传送到 Bot 区域，以原生正常行走方式标记 Bot 路径。
4. 玩家可长按快捷键，从保存的训练位置预览 Bot 点位；松开后返回编辑视角。
5. Bot 按预设模式移动。
6. Bot 到达终点或死亡后，本轮结束，玩家回到训练点并等待下一次手动开始。
7. 配置可按地图和名称持久化，以便重复训练。

## 3. 核心设计决策

### 3.1 不修改官方地图

项目以服务器插件运行在官方地图上，不发布七张地图的修改副本。地图白名单仅限制功能入口，训练逻辑本身保持地图无关。

### 3.2 配置阶段由玩家本人正常行走

编辑 Bot 点位时，将玩家 Pawn 临时传送到 Bot 区域。玩家使用 CS2 原生移动系统，因此自动获得正常行走、静步、跳跃、蹲伏、斜坡、台阶和墙体碰撞。

不在首版中实现“摄像机模拟玩家行走”，避免重新实现完整的玩家移动物理。

### 3.3 摄像机只用于长按预览

玩家在 Bot 区域编辑时，长按预览键启用固定在训练点的摄像机；松开后禁用摄像机，视角自然返回仍位于 Bot 区域的玩家 Pawn。

不要求同时显示两个实时画面。

### 3.4 Bot 路径使用清晰命名

避免训练点 `A` 与 Bot 路径点 `a` 淆混，内部统一使用：

- `PlayerAnchor`：玩家训练点。
- `EditAnchor`：Bot 区域的编辑入口。
- `BotStart`：Bot 起点。
- `BotEnd`：Bot 最终横拉终点。
- `BotJiggle`：Bot 小身位摆动点。
- `BotFacingYaw`：Bot 固定朝向。

### 3.5 首版优先确定性

Bot 首版只支持经过验证的地面直线路径，并默认复用 `CS2-Bot-Controller` 的原生 usercmd 运动控制。Bot 通过游戏原生移动物理完成加速、碰撞、动画和命中箱更新；本项目不把逐 tick 修改 Pawn 坐标作为首选实现。

本项目自研范围集中在 AWP 专用配置工作流、长按摄像机预览、训练状态机、路径编排、配置持久化和异常恢复。Bot 录制/回放能力作为后续扩展，不替代首版清晰可控的 a/b/c 状态机。

## 4. 建议技术架构

### 4.1 主体技术

- 自研 CounterStrikeSharp/C# 插件：命令、玩家会话、传送、AWP 配置工作流、Bot 生命周期、配置存储、倒计时和训练状态机。
- CS2 实体接口：固定摄像机、玩家和 Bot Pawn、幽灵 Bot 与点位验证。
- `CS2-Bot-Controller`：作为默认 Bot 运动后端，提供 Bot AI 锁定、原生 usercmd 模拟移动、输入抑制以及可选的玩家运动录制/回放。
- 可选自研原生兼容层：仅在 CounterStrikeSharp 无法稳定访问 `cs_player_camera` 时使用；不重复开发已经由 `CS2-Bot-Controller` 提供的 Bot 运动能力。

### 4.2 自研与复用边界

自研部分：

- AWP 点位配置命令与权限控制。
- EditAnchor、PlayerAnchor、BotStart、BotEnd、BotJiggle 和 BotFacing 数据模型。
- 玩家正常行走编辑和长按 A 点摄像机预览。
- 直接横拉与随机 jiggle 后横拉的状态机。
- 倒计时、随机等待、死亡/到达终点重置。
- 配置保存、七图白名单、路径验证和故障保护。

复用部分：

- `Lock(All)`/`Unlock`：关闭 Bot 自主 AI，避免与训练控制竞争。
- `StartUsercmdMovement`、`UpdateUsercmdMovement`、`CancelUsercmdMovement`：持续注入原生前后/横向模拟输入。
- `SuppressUsercmd` 或持久输入抑制：禁止 Bot 攻击、跳跃或其他不需要的动作。
- `StartRecord`、`StopRecord`、`TransferRecordingToReplay`、`StartReplay`：作为未来“录制真人路径”模式的扩展能力。

集成采用 `BotControllerApi` 的共享接口，不复制其原生实现代码。基线候选为 v0.6.1/ABI 18；实际部署必须以安装包公开的 ABI 和 API 合同为准，并在运行时检查兼容性。

如果依赖缺失、ABI 不匹配或原生 PlayerRunCommand hook 不可用，插件应拒绝启动训练并给出明确诊断，不得静默退化为逐 tick Teleport。

### 4.3 模块划分

```text
AwperTrainerPlugin
├── CommandController
├── SessionManager
├── SetupController
├── PreviewCameraController
├── BotController
├── BotControllerAdapter
├── BotMotionController
├── TrainingStateMachine
├── ProfileRepository
├── MapPolicy
├── CompatibilityGate
└── Diagnostics
```

职责：

- `CommandController`：注册命令和快捷键入口，检查当前状态是否合法。
- `SessionManager`：维护每名玩家的配置与训练运行状态。
- `SetupController`：记录锚点、传送编辑者、验证点位。
- `PreviewCameraController`：处理长按预览、释放和异常清理。
- `BotController`：创建、重生、冻结、保护、移除 Bot。
- `BotControllerAdapter`：封装 `IBotControllerApi`、ABI 检查、锁、运动 token、输入抑制和回放接口。
- `BotMotionController`：把世界路径转换为 Bot 本地 forward/left 输入，执行直接横拉和摆动后横拉。
- `TrainingStateMachine`：统一管理倒计时、随机延迟、运动、结束和重置。
- `ProfileRepository`：按地图和配置名称持久化训练参数。
- `MapPolicy`：维护七图白名单，并验证当前地图是否允许运行。
- `CompatibilityGate`：在插件加载和每轮启动前验证摄像机能力、BotController 能力与 ABI。
- `Diagnostics`：日志、路径验证结果、状态查询和故障保护。

### 4.4 许可证与产品化约束

`CS2-Bot-Controller` 从 v0.4.8 起采用 AGPL-3.0。原型和发布前必须明确：

- 开源社区项目应满足 AGPL-3.0 的源码提供与其他义务。
- 闭源分发、闭源托管服务或不满足 copyleft 的专有集成，应先与上游作者确认商业许可。
- 在商业模式和授权路径未确定前，不把该依赖视为没有约束的普通二进制库。
- 此处仅记录工程风险，正式发布前应由项目负责人获得适当的许可证确认。

## 5. 数据模型

建议使用 JSON 存储，配置目录与插件代码分离。

```json
{
  "schemaVersion": 1,
  "mapName": "de_example",
  "profileName": "example_awp_angle",
  "editAnchor": {
    "position": { "x": 0, "y": 0, "z": 0 },
    "angles": { "pitch": 0, "yaw": 0, "roll": 0 }
  },
  "playerAnchor": {
    "pawnPosition": { "x": 0, "y": 0, "z": 0 },
    "eyePosition": { "x": 0, "y": 0, "z": 64 },
    "eyeAngles": { "pitch": 0, "yaw": 0, "roll": 0 },
    "stance": "standing"
  },
  "botPath": {
    "start": { "x": 0, "y": 0, "z": 0 },
    "end": { "x": 0, "y": 0, "z": 0 },
    "jiggle": { "x": 0, "y": 0, "z": 0 },
    "facingYaw": 0,
    "stance": "standing"
  },
  "training": {
    "mode": "direct_peek",
    "countdownSeconds": 3.0,
    "randomDelayMinSeconds": 0.5,
    "randomDelayMaxSeconds": 3.0,
    "targetSpeed": 250.0,
    "jiggleCountMin": 1,
    "jiggleCountMax": 4,
    "jiggleEndpointPauseMinSeconds": 0.05,
    "jiggleEndpointPauseMaxSeconds": 0.2,
    "completionRadius": 4.0,
    "runTimeoutSeconds": 10.0
  }
}
```

运行时会话另行保存，不写入配置文件：

- 当前工作模式和状态机状态。
- 编辑前玩家位置、视角、速度和生存状态。
- 预览键是否按下。
- 当前 Bot Controller/Pawn 引用。
- 当前倒计时、随机延迟和运动阶段。
- 本轮随机数种子与摆动次数。

## 6. 命令与快捷键

命令名称可在实现时调整，首版建议：

| 命令 | 作用 | 允许状态 |
|---|---|---|
| `css_awper_set_edit_anchor` | 记录 Bot 编辑区域 | 空闲/配置 |
| `css_awper_set_player_anchor` | 记录训练点并传送到编辑区域 | 已记录 EditAnchor |
| `css_awper_set_bot_start` | 记录 BotStart | 编辑 |
| `css_awper_set_bot_end` | 记录 BotEnd | 编辑 |
| `css_awper_set_bot_jiggle` | 记录 BotJiggle | 编辑 |
| `css_awper_set_bot_facing` | 使用当前朝向覆盖自动朝向 | 编辑 |
| `css_awper_mode 1/2` | 选择训练模式 | 编辑/空闲 |
| `css_awper_validate` | 验证全部点位和路径 | 编辑/空闲 |
| `css_awper_save <name>` | 保存配置 | 验证通过后 |
| `css_awper_load <name>` | 加载当前地图配置 | 空闲 |
| `css_awper_list` | 列出当前地图配置 | 任意安全状态 |
| `css_awper_start` | 启动一轮训练 | IdleReady |
| `css_awper_abort` | 中止并恢复玩家 | 任意训练状态 |
| `+awper_preview` | 开始预览 PlayerAnchor | 编辑 |
| `-awper_preview` | 结束预览 | Previewing |

推荐客户端绑定：

```cfg
bind mouse4 +awper_preview
bind mouse5 css_awper_start
```

服务器不得未经玩家同意修改客户端按键绑定。

## 7. 配置阶段状态机

```text
SetupEmpty
  └─ set_edit_anchor → EditAnchorSaved
       └─ set_player_anchor → EditingBotPath
            ├─ set_bot_start
            ├─ set_bot_end
            ├─ set_bot_jiggle
            ├─ +preview ↔ PreviewingPlayerAnchor
            └─ validate → ProfileReady
                 └─ save → IdleReady
```

### 7.1 记录 EditAnchor

- 保存玩家 Pawn 位置和当前视角。
- 检查玩家存活、在允许地图上且不处于其他会话。

### 7.2 记录 PlayerAnchor

- 保存 Pawn 原点、眼睛位置和眼睛角度。
- 保存站姿。
- 清零速度并传送到 EditAnchor。
- 进入编辑保护：免疫伤害、禁止影响其他玩家、禁止触发回合目标。

### 7.3 标记 Bot 点

- 使用玩家脚下 Pawn 原点作为 Bot 点。
- 单独保存朝向；默认让 Bot 面向 PlayerAnchor。
- 每次记录后立即执行局部合法性检查并反馈结果。

### 7.4 长按预览

按下时：

1. 检查仅在编辑状态可用。
2. 保存编辑者速度并将速度归零。
3. 暂停玩家移动。
4. 在当前编辑位置同步一个幽灵 Bot。
5. 启用固定摄像机并移动到 PlayerAnchor 眼睛位置与角度。

松开时：

1. 禁用摄像机。
2. 移除或隐藏临时幽灵 Bot。
3. 恢复编辑者移动。

死亡、断线、换队、回合重置、地图结束或插件卸载时必须强制禁用摄像机，不能依赖 `-awper_preview` 一定到达。

## 8. 路径验证

保存配置前必须通过：

1. 所有必需点均已记录。
2. 每个点均能容纳站立 Bot 的玩家碰撞盒。
3. 每个点脚下存在可站立地面。
4. BotStart 到 BotEnd 的玩家碰撞盒扫描无阻挡。
5. 模式二中 BotStart 到 BotJiggle 的扫描无阻挡。
6. 路径高度差、地面法线和长度在首版支持范围内。
7. PlayerAnchor 不在实体内部。
8. PlayerAnchor 到关键 Bot 点的视线检查结果可用，但不强制全部可见。

建议首版限制：

- 路径长度：16–1024 units。
- 路径最大高度差：可配置，默认不超过一个小台阶。
- 不支持梯子、跳跃、下落和水中路径。
- 路径不合法时拒绝保存，而不是训练时尝试自动绕路。

## 9. 训练状态机

```text
IdleReady
  └─ start → Prepare
       └─ success → Countdown3s
            └─ elapsed → RandomDelay
                 └─ elapsed → BotMoving
                      ├─ bot killed → Finish
                      ├─ bot reaches end → Finish
                      ├─ player killed → Abort
                      └─ timeout/stuck → Abort
                           Finish/Abort → Reset → IdleReady
```

### 9.1 Prepare

- 将玩家传送到 PlayerAnchor Pawn 原点和保存的角度。
- 清零玩家速度。
- 重置生命值、护甲、弹药、后坐力和需要的武器状态。
- 创建或重生训练 Bot。
- 将 Bot 放到 BotStart 并冻结。
- Bot 在正式启动前不可伤害；必要时隐藏或阻止向训练者传输。
- 清理上一轮遗留定时器和实体。

### 9.2 Countdown3s

- 固定倒计时三秒。
- 玩家保持完全可操作，可移动、开镜和预瞄。
- Bot 保持冻结和保护状态。

### 9.3 RandomDelay

- 均匀随机等待 0.5–3.0 秒。
- 玩家继续保持完全可操作。
- 延迟结束时解除 Bot 保护并开始运动。

### 9.4 BotMoving

- 模式一：BotStart → BotEnd。
- 模式二：BotStart ↔ BotJiggle 完整摆动 1–4 次，然后 BotStart → BotEnd。
- Bot 身体朝向默认固定为面向 PlayerAnchor 的 yaw。
- Bot 在任意运动阶段死亡均立即进入 Finish。
- 到达终点采用半径判定，不比较浮点坐标是否完全相等。

### 9.5 Finish 和 Reset

- 默认提供 0.2 秒可配置的命中反馈延迟；可设为 0 实现立即重置。
- 停止 Bot，注销本轮运动回调并移除 Bot。
- 禁用任何残留摄像机。
- 将玩家传送回 PlayerAnchor，并恢复初始视角和零速度。
- 恢复生命值、弹药和后坐力。
- 返回 IdleReady，等待下一次快捷键，不自动连续开局。

## 10. Bot 运动设计

### 10.1 默认运动后端：CS2-Bot-Controller

使用真实 Bot Pawn，并通过 `CS2-Bot-Controller` 的 `Lock(All)` 禁用自主 AI。上层状态机维护：

- 当前路径段。
- 当前位置和目标点。
- 当前 usercmd movement token。
- Bot 固定朝向。
- 端点停顿和剩余摆动次数。

每个路径段：

1. 计算到目标点的水平单位方向。
2. 根据固定的 `BotFacingYaw`，把世界方向投影为 Bot 本地 `forwardMove` 和 `leftMove`。
3. 调用 `StartUsercmdMovement`；方向需要变化时调用 `UpdateUsercmdMovement`。
4. 让游戏原生移动系统处理加速、摩擦、碰撞、台阶、动画和命中箱。
5. 每 tick 只负责检测 Bot 是否死亡、卡住、超时或进入目标点半径。
6. 到达端点后取消或更新 movement token，并切换下一运动阶段。
7. 本轮结束、插件卸载、地图切换或异常时必须取消全部 movement/suppression token 并解除锁。

训练期间通过输入抑制阻止 Bot 攻击、使用、跳跃和其他未授权动作。禁止以低频大跨度 Teleport 替代运动后端。

### 10.2 可选录制/回放模式

`CS2-Bot-Controller` 能记录玩家的 origin、velocity、view angles、button states、duck、ladder、active weapon 和 subtick input，并将记录回放到 Bot。

该能力不属于首版必需路径，但可用于：

- 录制真人式起步、急停、蹲起或复杂 peek。
- 对比算法横拉与真人录制路径。
- 未来支持超过 a/b/c 三点的复杂动作。

录制/回放不得与 usercmd 路径控制同时作用于同一 Bot。`BotControllerAdapter` 必须保证两种后端互斥。

### 10.3 速度语义

实施前固定以下产品默认值：

- `targetSpeed = 250 units/s`，表示训练工具定义的满地速。
- Bot 默认站立并面向 PlayerAnchor 横向移动。
- usercmd 后端使用游戏原生加速；如要求 Bot 在可见前已经达到满地速，应在 BotStart 之前增加隐藏预加速距离，或把 BotStart 定义为预加速起点。
- 后续可增加按手持武器决定最大速度的模式。

### 10.4 模式二语义

- 一次摆动严格定义为 `BotStart → BotJiggle → BotStart`。
- 完整摆动次数均匀随机为 1、2、3 或 4。
- 每个端点可随机停顿 0.05–0.20 秒。
- 完成摆动后必须回到 BotStart，再执行 BotStart → BotEnd。
- 每轮保存随机种子，方便诊断异常行为。

### 10.5 运动质量与依赖关卡

建立独立原型验证 `CS2-Bot-Controller` 是否满足训练质量。重点检查：

- 原生 usercmd 横拉能否稳定达到目标速度。
- 固定朝向时世界路径到 forward/left 的投影是否准确。
- 快速反向 jiggle 的加速、急停、动画和命中箱是否自然。
- 不同 ping 与服务器负载下是否出现抖动或状态漂移。
- CS2 更新后 ABI、签名和 PlayerRunCommand hook 失效时是否能安全拒绝训练。

只有现有依赖无法满足需求且上游无法修复时，才评估自研兼容 Bot 运动模块；逐 tick Teleport 仅用于诊断，不作为产品后端。

## 11. Bot 与玩家规则

### 11.1 编辑阶段

- 编辑者免疫伤害。
- 不允许编辑者影响回合目标。
- 预览时冻结编辑者并清零速度。
- 必须提供 `css_awper_abort` 恢复入口。

### 11.2 训练阶段

- 玩家可进行移动、射击、开镜等任意正常操作。
- Bot 仅在 BotMoving 阶段可受到伤害。
- Bot 不允许自主瞄准或射击玩家。
- 玩家死亡时中止本轮并恢复会话。
- Bot 到达 BotEnd、死亡、卡住或超时均应得到唯一一次结束处理。

### 11.3 故障保护

- 所有定时器和 tick 回调绑定到 session generation/id，旧回调不得影响新一轮。
- 插件卸载、热重载和地图切换时恢复所有受控玩家。
- 摄像机句柄和 Bot 句柄每次使用前检查有效性。
- 预览释放事件丢失时，状态退出或会话超时必须强制关闭摄像机。

## 12. 配置持久化

建议目录：

```text
configs/AwperTrainer/
└── <map-name>/
    └── <profile-name>.json
```

规则：

- 配置名称只允许安全字符并限制长度。
- 保存采用临时文件加原子替换，避免崩溃留下半个 JSON。
- 配置包含 `schemaVersion`，为以后迁移预留。
- 加载时检查当前地图名称必须匹配。
- 地图更新后首次加载重新验证所有点位和路径。
- 首版配置由服务器管理员或具备指定权限的玩家创建和删除。

## 13. 里程碑

### M0：接口可行性原型

- 在一张官方地图上注册玩家命令。
- 保存、传送并恢复玩家 Pawn。
- 验证固定摄像机可按下启用、松开禁用。
- 验证死亡、换队和断线后摄像机能清理。
- 验证真实 Bot Pawn 可被稳定创建、传送、伤害和识别死亡。
- 安装候选版本的 `CS2-Bot-Controller` 及 CounterStrikeSharp API 包。
- 验证共享 capability 可以取得，并检查运行时 ABI；缺失或不兼容时安全拒绝训练。
- 验证 `Lock(All)` 能稳定关闭 Bot AI，解除锁后 Bot 能恢复。
- 验证 `StartUsercmdMovement`/`UpdateUsercmdMovement`/`CancelUsercmdMovement` 能完成 BotStart → BotEnd 与 BotStart ↔ BotJiggle。
- 验证固定 BotFacingYaw 下，世界方向能够正确投影为 forward/left 输入。
- 验证死亡、中止、热重载和换图会取消所有 movement/suppression token。
- 记录 AGPL 合规方案或商业授权处理人，不在授权路径不明时进入闭源发布。

完成条件：摄像机与 BotController 两条高风险接口均得到实机证据，并形成明确的版本/ABI/许可证决策记录。

### M1：配置闭环

- 实现 EditAnchor 和 PlayerAnchor。
- 实现玩家正常行走编辑。
- 实现 BotStart、BotEnd、BotJiggle 和 BotFacing。
- 实现长按 A 点预览和幽灵 Bot。
- 实现基础点位与路径验证。

完成条件：用户不编辑文件即可在一张地图完成一套路径配置。

### M2：模式一训练闭环

- 实现训练状态机。
- 实现三秒倒计时和 0.5–3 秒随机延迟。
- 实现 BotStart → BotEnd 运动。
- 实现死亡、到达、玩家死亡和超时重置。

完成条件：连续执行 100 轮无状态泄漏、重复定时器或摄像机残留。

### M3：模式二与参数化

- 实现 1–4 次随机摆动。
- 实现端点随机停顿。
- 实现模式和速度配置。
- 实现随机种子日志。

完成条件：固定种子可重复得到完全一致的运动序列。

### M4：配置存储与七图覆盖

- 实现保存、加载、列出和删除配置。
- 建立七图白名单。
- 在七张目标地图逐一验证传送、摄像机、Bot 和路径扫描。
- 加入地图更新后的配置重新验证。

完成条件：每张地图至少保存并稳定运行一套烟雾/墙体附近的典型 AWP 路径。

### M5：运动质量评估

- 对比 usercmd 运动与真人满速横拉的动画、速度、命中箱和网络表现。
- 在不同 ping 和服务器负载下测试。
- 验证依赖缺失、ABI 错误、hook 失效与 CS2 更新后的安全降级。
- 决定是否加入 BotController 的运动录制/回放模式。
- 只有上游能力无法满足需求时，才决定是否开发自研原生运动兼容层。

完成条件：书面确认生产依赖版本、升级策略、回放功能范围和原生兼容层决策。

## 14. 测试计划

### 14.1 功能测试

- 每个命令在正确与错误状态下的行为。
- 未记录前置锚点时拒绝后续操作。
- 长按、快速点按和重复按下预览键。
- 预览中死亡、换队、断线、回合结束和插件热重载。
- Bot 在摆动、最终横拉和端点到达时死亡。
- Bot 到达阈值边界和路径被动态实体阻挡。
- 玩家在倒计时与随机等待期间自由移动。
- 连续启动、主动中止和重新加载配置。

### 14.2 运动与命中测试

- 服务器记录每 tick Bot 位置、速度和目标速度。
- 检查移动过程速度是否达到设定值。
- 检查模型、命中箱和服务器位置是否一致。
- 检查高速移动时子弹命中和死亡事件是否可靠。
- 检查 30、60、100 ms 网络延迟下的观察效果。

### 14.3 稳定性测试

- 单配置连续运行 1000 轮。
- 七张地图各运行至少 100 轮。
- 无残留 Bot、摄像机、计时器或玩家保护状态。
- 配置文件损坏、缺字段和旧 schemaVersion 的错误处理。
- BotController 缺失、ABI 不匹配或 capability 获取失败时不启动训练，且不影响普通服务器运行。
- 每次正常结束、主动中止、玩家断线、插件热重载和地图切换后均无残留 movement/suppression token。

## 15. MVP 验收标准

满足以下全部条件才视为 MVP 完成：

1. 在白名单官方地图中无需修改地图即可运行。
2. 用户能完整记录 EditAnchor、PlayerAnchor、BotStart、BotEnd 和 BotJiggle。
3. 编辑阶段为原生正常行走，不是自由飞行摄像机。
4. 长按预览键稳定显示固定 PlayerAnchor 视角，松开后准确返回编辑视角。
5. 模式一和模式二均可执行，并符合定义的路径顺序。
6. 每轮包含三秒倒计时及 0.5–3 秒随机延迟。
7. 训练期间玩家可以自由移动和射击。
8. Bot 死亡或到达 BotEnd 后只触发一次重置。
9. 玩家被恢复到 PlayerAnchor 的位置、视角和零速度，并等待手动开始下一轮。
10. 100 轮连续训练无卡住、重复 Bot、摄像机残留和旧定时器串轮。
11. 配置可以保存、加载并在地图不匹配或路径失效时安全拒绝运行。
12. Bot 默认通过 `CS2-Bot-Controller` 原生 usercmd 后端移动，不使用逐 tick Teleport 作为产品实现。
13. BotController 缺失或 ABI 不兼容时，插件明确报错并安全禁用训练功能。

## 16. 暂不纳入首版

- Alt 自定义前端。
- 左下角实时摄像机 PIP。
- 多名玩家同时拥有彼此不可见的独立训练 Bot。
- Bot 跳跃、蹲起切换、梯子、复杂 NavMesh 路径。
- 自动识别所有比赛常见架点。
- 云端配置分享与排行榜。
- 完整录像、命中统计和训练分析。

## 17. 需要在 M0 锁定的技术问题

1. 当前服务器框架能否稳定访问玩家的 `cs_player_camera`；若不能，采用何种兼容层。
2. 长按 `+/-` 自定义命令在目标客户端与服务器组合中是否稳定送达。
3. 固定摄像机预览时，本地 Pawn 是否会被渲染；幽灵 Bot 是否必须始终使用独立实体。
4. 候选 `CS2-Bot-Controller` 版本、实际 ABI 与 CounterStrikeSharp/.NET 版本是否兼容。
5. `Lock(All)` 是否能完全阻止 Bot AI 与 usercmd 运动竞争，并在异常退出后正确恢复。
6. `StartUsercmdMovement` 的 analog 输入范围、原生最大速度、加速时间和快速反向行为是否符合训练要求。
7. 世界路径方向如何稳定投影到固定 BotFacingYaw 的 forwardMove/leftMove。
8. movement/suppression token 在死亡、中止、断线、热重载和换图时如何统一回收。
9. 编辑保护和训练回合规则如何避免官方回合系统自动结束。
10. 目标“满地速”采用固定 250 units/s，还是按 Bot 手持武器最大速度计算。
11. 项目将按 AGPL 开源发布，还是需要取得 `CS2-Bot-Controller` 的单独商业许可。

## 18. 推荐实施顺序

先完成 M0，不要先制作 UI 或七图配置。摄像机访问、BotController ABI/capability、原生 usercmd 运动质量和许可证路径是决定架构的高风险点。M0 得到实机结论后，按 M1 → M2 → M3 → M4 顺序完成自研 AWP 工作流；M5 再决定是否增加运动录制/回放，或在上游无法满足需求时开发自研原生兼容层。
