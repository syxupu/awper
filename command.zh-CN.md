# AWPER 1.1.0 指令手册

[English](command.md) · [Русский](command.ru.md) · [中文](command.zh-CN.md)

本文列出当前版本全部玩家可使用的 AWPER 指令，包括按键、点位放置、配置管理、摄像机、训练控制和地图切换。

从本版本开始，所有插件指令都已取消 `awper` 前缀。例如旧指令 `!awper_start` / `css_awper_start` 现改为 `!start` / `css_start`；旧指令不再注册。`exec awper_bindings` 中的 `awper_bindings` 是配置文件名，因此不受此变更影响。

聊天框中以 `!` 或 `/` 开头；控制台中使用完整的 `css_` 前缀。例如以下三条等价：

```text
!start
/start
css_start
```

## 一、常用按键

| 按键 | 作用 | 对应指令 |
|---|---|---|
| `F5` | 打开或关闭 AWPER 中央菜单 | `css_ui` |
| `Mouse4` | 点击切换摄像机预览 | `css_preview_toggle` |
| `Mouse5` | 开始一轮训练 | `css_start` |

如果绑定未加载，在控制台执行：

```text
exec awper_bindings
```

也可以手动绑定：

```text
bind "F5" "css_ui"
bind "MOUSE4" "css_preview_toggle"
bind "MOUSE5" "css_start"
```

## 二、帮助与前端菜单

| 聊天框指令 | 控制台指令 | 作用 |
|---|---|---|
| `!help` | `css_help` | 显示简要帮助 |
| `!ui` | `css_ui` | 打开或关闭 AWPER 中央菜单 |

打开菜单后，使用屏幕对应的数字键选择。再次按 `F5` 关闭。

F5 主菜单目前包含：

1. 开始一轮训练
2. 切换摄像机预览
3. 加载本地图配置
4. 显示当前状态
5. 设置点位、模式和速度
6. 切换训练地图
7. 中止训练并恢复

F5 菜单调用的仍然是本文列出的同一批指令，因此不会绕过权限和配置验证。

## 三、放置训练点位

点位编辑指令需要 `@css/config` 管理权限，并且玩家必须存活。

### 1. 进入编辑模式并声明轨道名称

```text
!edit <轨道名称>
```

例如：

```text
!edit mirage_awp_1
```

作用：

- 创建一个命名编辑会话；未执行该指令时，所有标点、模式、速度、验证和保存指令都会被拒绝。
- 名称长度为 1～64 个字符，只允许英文字母、数字、下划线 `_` 和连字符 `-`。
- 如果名称已经存在，插件会提示保存时将覆盖原轨道。
- 一个玩家不能同时存在两个配置会话；需要先执行 `!abort` 才能重新进入编辑模式。

### 2. 记录编辑入口 EditAnchor

```text
!set_edit_anchor
```

控制台形式：

```text
css_set_edit_anchor
```

作用：

- 记录玩家当前脚下位置和视角方向。
- 这是点位编辑的入口位置。
- 记录 PlayerAnchor 后，插件会把玩家传送回这里，以便继续步行放置 Bot 路径。
- 必须先使用 `!edit <轨道名称>` 进入编辑模式。

推荐选择安全、方便移动到 Bot 路径的位置。

### 3. 记录玩家训练点 PlayerAnchor

```text
!set_player_anchor
```

作用：

- 记录玩家训练时站立的位置。
- 记录摄像机眼睛位置。
- 记录玩家当时的视角方向。
- 以后摄像机预览会固定在这里。
- 记录完成后，玩家会自动传送回 EditAnchor。

正确操作：

1. 先执行 `!edit <轨道名称>`。
2. 执行 `!set_edit_anchor`。
3. 走到实战中 AWP 玩家应该站的位置。
4. 将准星对准预期的 Bot 出现方向。
5. 执行 `!set_player_anchor`。

### 4. 记录 Bot 起点 BotStart

```text
!set_bot_start
```

作用：

- 把玩家当前脚下位置记录为 Bot 起点。
- Bot 每轮生成后会被放到这里。
- 插件会立即进行站立空间、地面和平整度探测。

应当站在 Bot 横拉开始前、掩体内侧的位置执行。

### 5. 记录 Bot 终点 BotEnd

```text
!set_bot_end
```

作用：

- 把玩家当前脚下位置记录为 Bot 最终移动目标。
- 直拉模式的主要轨迹为：

```text
BotStart ─────────────→ BotEnd
```

Bot 到达终点附近后，本轮训练结束。

### 6. 记录急停或晃身点 BotJiggle

```text
!set_bot_jiggle
```

作用：

- 记录急停模式使用的中间位置。
- 只有模式 `2` 强制要求这个点。
- 直拉模式可以不设置。

急停模式大致逻辑：

```text
BotStart ⇄ BotJiggle
    重复 1～4 次
BotStart ─────────→ BotEnd
```

### 7. 记录 Bot 面向方向

```text
!set_bot_facing
```

作用：

- 读取玩家当前视角的水平角度。
- 把这个角度保存为 Bot 移动时的面向方向。
- 只记录 Yaw，不记录玩家站立的位置。

操作时将视角转向希望 Bot 面对的方向，然后执行该指令。这个字段不是必须手动设置；如果省略，插件会自动让 Bot 从起点面向 PlayerAnchor。

## 四、训练模式

### 直拉模式

```text
!mode 1
```

内部名称为 `DirectPeek`，轨迹为：

```text
BotStart ─────────→ BotEnd
```

这是默认模式，不要求 BotJiggle。

### 急停后横拉模式

```text
!mode 2
```

内部名称为 `JiggleThenPeek`。运行逻辑：

1. Bot 在 BotStart 与 BotJiggle 之间随机晃动。
2. 默认随机重复 1～4 次。
3. 每次端点停顿约 0.05～0.20 秒。
4. 最后从 BotStart 正式移动到 BotEnd。

模式 2 必须先记录：

```text
!set_bot_jiggle
```

## 五、Bot 移动速度

格式：

```text
!speed <1-215>
```

可用范围为 `1–215 units/s`。

| 指令 | 速度 | 用途 |
|---|---:|---|
| `!speed 215` | 215 | AK-47 正常持枪全速移动，也是默认值 |
| `!speed 180` | 180 | 中等速度 |
| `!speed 150` | 150 | 慢速练习 |

该值是插件的目标地面速度上限，不是简单修改 Bot AI 速度。Bot 会从静止开始，按服务器当前的 `sv_accelerate`、`sv_friction`、`sv_stopspeed` 和 tick interval 逐帧加速；因此很短的轨道可能在达到目标速度前就已经抵达终点。

## 六、验证与保存配置

### 验证当前配置

```text
!validate
```

作用：

- 检查必需点位是否完整。
- 检查当前地图是否匹配。
- 检查 BotStart、BotEnd、BotJiggle 的站立空间和地面。
- 检查目标速度是否合法。
- 检查急停模式是否存在 BotJiggle。
- 进行实时 RayTrace 地图几何验证。

视线不直接可见只会产生 `los.start` 或 `los.end` 警告，不会阻止保存，因为 Bot 起点可以位于掩体后。

### 保存配置

```text
!save
```

保存使用 `!edit <轨道名称>` 进入编辑模式时声明的名称，不再接收名称参数。

保存时插件会自动：

1. 构建完整配置。
2. 执行实时地图验证。
3. 写入当前地图配置目录。
4. 自动加载刚保存的配置。
5. 恢复玩家到 PlayerAnchor。
6. 退出编辑模式。

必需字段：

- EditAnchor
- PlayerAnchor
- BotStart
- BotEnd

模式 2 额外要求 BotJiggle。BotFacing 可以省略。

## 七、配置文件管理

### 列出当前地图所有配置

```text
!list
```

只显示当前地图的配置。例如，在 `de_mirage` 不会显示 `de_dust2` 的配置。

### 加载配置

```text
!load <配置名称>
```

例如：

```text
!load mirage_awp_1
```

加载时插件会重新进行当前地图的实时验证。加载成功后还不能直接开始，必须完成一次摄像机验证：

1. 点击 `Mouse4` 进入预览。
2. 检查摄像机和 Bot 幽灵位置。
3. 再点击一次 `Mouse4` 退出预览。
4. 点击 `Mouse5` 或执行 `!start`。

### 删除配置

```text
!delete <配置名称>
```

例如：

```text
!delete mirage_awp_1
```

要求：

- 拥有 `@css/config` 权限。
- 只能删除当前地图的配置。
- 如果删除的配置正在某个编辑会话中使用，插件会清除其已加载状态。

### 复制配置并修改速度

```text
!copy <原名称> <新名称> <1-215>
```

例如：

```text
!copy mirage_awp_1 mirage_awp_slow 150
```

该操作只修改复制品的轨道名称和 Bot 目标速度。地图、所有锚点、Bot 路径、面向、移动模式、倒计时、随机延迟及其他训练参数均保持不变。

要求：

- 拥有 `@css/config` 权限。
- 原轨道属于当前地图并且确实存在。
- 新名称必须符合轨道命名规则，而且不能与已有轨道重名；指令不会覆盖目标轨道。
- 速度范围为 `1–215 units/s`。
- 复制完成后不会自动加载复制品，也不会改变当前已加载的轨道。

## 八、摄像机预览

### 点击切换预览

```text
!preview_toggle
```

或点击 `Mouse4`。

逻辑：

- 第一次执行：进入固定摄像机视角。
- 没有第二次执行：一直保持摄像机视角。
- 第二次执行：退出摄像机并完成本次会话验证。

预览画面：

- 摄像机固定在 PlayerAnchor。
- 摄像机朝向 Bot 点位。
- 幽灵模型标记 Bot 的预期位置。

### 强制进入预览

```text
!preview_on
```

### 强制退出预览

```text
!preview_off
```

成功退出后，当前会话会被标记为：

```text
camera=verified-this-session
```

### 旧版长按兼容指令

控制台或绑定可以继续使用：

```text
+preview
-preview
```

例如：

```text
bind "MOUSE4" "+preview"
```

不过当前默认绑定已经改为点击切换：

```text
bind "MOUSE4" "css_preview_toggle"
```

预览至少要求已经记录或加载 PlayerAnchor。新建配置时，只要 EditAnchor 和 PlayerAnchor 已存在，就可以提前查看摄像机，不必等 BotStart、BotEnd 全部设置完成。

开启预览时，摄像机固定在 PlayerAnchor 并看向 BotStart；玩家 Pawn 的朝向则冻结为按下 `Mouse4` 那一刻的朝向，不会被摄像机角度或 PlayerAnchor 保存的视角改写。退出预览后仍恢复该朝向。

## 九、开始和终止训练

### 开始一轮训练

```text
!start
```

或点击 `Mouse5`。

开始条件：

- 玩家存活。
- 当前地图属于白名单。
- 已加载通过验证的配置。
- BotController ABI 兼容。
- RayTrace 验证通过。
- 本次会话已经完成摄像机预览验证。
- 当前没有另一轮训练正在运行。

开始后：

1. 关闭 F5 菜单和摄像机。
2. 清理现有 Bot。
3. 创建敌方 Bot。
4. 给 Bot 配置 AK-47。
5. 等待 Bot Pawn 和模型稳定。
6. 进入 3 秒倒计时。
7. 再等待约 0.5～3.0 秒随机延迟。
8. Bot 按配置轨迹移动。
9. Bot 被击杀或到达终点后完成本轮。
10. 插件清理本轮 Bot、掉落 AK 和相关实体。

### 以指定速度强制开始一轮

```text
!start_speed <1-215>
```

例如：

```text
!start_speed 150
```

该指令与 `!start` 使用相同的已加载轨道和启动条件，但只在本轮运行时把 Bot 目标速度替换为指定值。它不会修改磁盘中的轨道，也不会修改当前会话加载的原轨道；本轮结束后再次执行 `!start`，仍会恢复使用轨道原本保存的速度。

### 中止训练或编辑

```text
!abort
```

作用：

- 中止当前训练轮次。
- 关闭 F5 菜单。
- 退出摄像机预览。
- 清除当前玩家的点位编辑会话。
- 将玩家恢复到可恢复位置。
- 清理训练 Bot 和插件持有的 AK。

如果想从头重新放置点位，也应先执行这条指令。

## 十、查看运行状态

```text
!status
```

显示内容包括：

- `bot=`：BotController 兼容状态。
- `world=`：RayTrace 地图验证状态。
- `camera=`：本次会话是否完成摄像机验证。
- `editing=`：当前正在编辑的命名轨道；未编辑时为 `none`。
- `runtime=`：当前训练状态。
- `native=`：BotController 原生调用诊断信息。

常见状态：

```text
camera=unverified-this-session
camera=verified-this-session
editing=mirage_awp_1
editing=none
runtime=none
runtime=Prepare
runtime=Countdown
runtime=Running
```

## 十一、地图指令

### 列出可用地图

```text
!maps
```

当前默认地图池：

```text
dust2
inferno
mirage
anubis
ancient
nuke
cache
```

输出会标记当前地图。

### 切换地图

格式：

```text
!map <地图>
```

可以省略 `de_`：

```text
!map mirage
!map dust2
!map cache
```

也可以使用完整名称：

```text
!map de_mirage
!map de_dust2
```

完整可用指令：

```text
!map dust2
!map inferno
!map mirage
!map anubis
!map ancient
!map nuke
!map cache
```

切图权限：

- `@css/changemap`，或
- `@css/config`。

切图时插件会：

1. 关闭所有玩家的 AWPER 菜单。
2. 关闭所有摄像机。
3. 中止当前训练。
4. 恢复并清除所有编辑会话。
5. 执行 `changelevel de_<地图>`。

## 十二、权限和使用条件汇总

需要 `@css/config` 权限的指令：

```text
!edit <名称>
!set_edit_anchor
!set_player_anchor
!set_bot_start
!set_bot_end
!set_bot_jiggle
!set_bot_facing
!mode
!speed
!validate
!save
!copy <原名称> <新名称> <1-215>
!delete
```

切换地图需要 `@css/changemap` 或 `@css/config`：

```text
!map <地图>
```

以下操作要求玩家当前存活：

- 打开 F5 菜单。
- 放置和修改点位。
- 加载配置。
- 摄像机预览。
- 开始训练。

## 十三、完整的新点位制作流程

以 `mirage_awp_1` 为例。

先清除旧会话：

```text
!abort
```

声明轨道名称并进入编辑模式：

```text
!edit mirage_awp_1
```

站在方便编辑和行走的位置：

```text
!set_edit_anchor
```

走到玩家实际架枪位置，调整好视角：

```text
!set_player_anchor
```

插件传送回编辑入口后，走到 Bot 起点：

```text
!set_bot_start
```

走到 Bot 终点：

```text
!set_bot_end
```

如果需要急停模式，走到急停位置：

```text
!set_bot_jiggle
!mode 2
```

如果只需要两点横拉：

```text
!mode 1
```

调整视角并记录 Bot 面向：

```text
!set_bot_facing
```

设置正常 AK 速度：

```text
!speed 215
```

验证并保存：

```text
!validate
!save
```

摄像机验证：

```text
!preview_toggle
!preview_toggle
```

开始训练：

```text
!start
```

下一轮直接再次执行：

```text
!start
```

## 十四、完整指令速查表

```text
!help
!ui
!maps
!map <dust2|inferno|mirage|anubis|ancient|nuke|cache>

!edit <名称>
!set_edit_anchor
!set_player_anchor
!set_bot_start
!set_bot_end
!set_bot_jiggle
!set_bot_facing

!mode <1|2>
!speed <1-215>
!validate
!save

!list
!load <名称>
!copy <原名称> <新名称> <1-215>
!delete <名称>

!preview_on
!preview_off
!preview_toggle

!start
!start_speed <1-215>
!abort
!status
```

控制台或按键绑定兼容指令：

```text
+preview
-preview
exec awper_bindings
```
