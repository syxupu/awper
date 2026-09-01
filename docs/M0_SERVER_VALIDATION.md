# M0 目标服务器验证清单

该清单必须在将要部署的 Windows/Linux、CounterStrikeSharp、CS2 与 BotController 组合上执行。每项记录服务器 build、插件 commit、时间、地图、日志和最少一段视频或 tick CSV；失败时保留 `css_status` 输出。

## 1. 安装与基线

1. 安装 CounterStrikeSharp 1.0.373+、CS2-Bot-Controller 与 FUNPLAY Ray-Trace 的原生包、CSS 实现和共享 API 包。
2. 确认共享目录中只有上游随包提供的 `BotControllerApi.dll` 与 `RayTraceApi.dll`；不要部署本仓库的 build-time contract DLL。
3. 加载 `AwperTrainer.dll` 与 `AwperTrainer.Core.dll`，运行 `css_status`。
4. 先运行包内 `Verify-ServerInstall.ps1 -CsgoDirectory <game/csgo>`，再运行 `css_status`。期望 Bot capability 报告 ABI 19、RayTrace capability attached；缺失、ABI 不符或 native hook/trace 不可用时训练必须保持禁用。

## 2. Camera 证据

客户端推荐绑定：

```cfg
bind mouse4 "css_preview_toggle"
```

1. 只记录 EditAnchor 和 PlayerAnchor；此时不记录 BotStart、BotEnd 或 BotJiggle，并走到 Bot 区域。
2. 第一次点击 Mouse4：玩家速度归零、Pawn 输入锁定、视角位于 PlayerAnchor；没有第二次点击时必须一直保持。该步骤在 Bot 点位缺失时也必须工作。
3. 第二次点击 Mouse4：Pawn 专属 camera 禁用、Pawn 解锁、视角回到 Bot 区域，位置未漂移。
4. 确认编辑位置出现半透明独立幽灵实体；快速点按、重复按下，并分别在预览中死亡、换队、断线、回合结束、换图、热重载。
5. 每次检查 ghost prop 数量归零；Pawn 专属 `cs_player_camera` 可以由引擎保留，但 `m_bEnabled` 必须为 false。记录本地 Pawn 是否仍被渲染。

实现说明：NuGet 1.0.373 尚未暴露 2026-08-24 新增的 `CSPlayerPawn.GetCamera()`。插件随包部署一个编译后的 `cs_script` 桥，通过官方 `GetCamera()/SetEnabled()/SetIsControllingAngles()` 获取并启用 Pawn 专属摄像头；C# 只通过公共 `Schema` API识别其所有者和启用状态并设置位置。

## 3. BotController 证据

1. 放置一个真实 Bot，确认 `Lock(All)` 后 AI 停止，`Unlock` 后恢复。
2. 以 FacingYaw 0/90/180/-90 测试四个世界方向，核对 forward/left 投影。
3. 测试 Start→End、Start↔Jiggle；analog 输入范围应为 `[-1, 1]`。
4. 在运动中击杀 Bot、Abort、断线、热载、换图；每次确认 movement/suppression token 取消且 All lock 解除。
5. 在 `AwperTrainer.json` 设置 `EnableMotionCsv=true`；用 `evidence/*.csv` 的 origin/velocity/target_speed/distance_to_target 检查目标速度、反向和卡住判定，并结合录像检查动画、服务器命中箱和子弹命中。

上游基线：CS2-Bot-Controller commit `2c4727699fcfd7afd426ab78d8c18b424d14877b`（2026-08-20）。该提交的 `TECH.md` 顶部仍写 ABI 17，但原生托管包装器实际声明 `ExpectedAbiVersion = 19`；部署以安装包 `IBotControllerApi.AbiVersion` 与包装器合同为准，不使用旧计划中的 ABI 18。

## 4. 世界几何探针

本项目接入 FUNPLAY-pro-CS2/Ray-Trace 的 `raytrace:craytraceinterface` 公共 capability，并通过以下实时调用完成保存前和每次加载/启动前验证：

- 站立玩家 hull 是否可容纳；
- 向下地面 trace 与 normal Z；
- Start→End、Start→Jiggle 的 swept hull；
- PlayerAnchor 到关键点的 LOS；
- 当前地图标识；无论保存时标识是否相同，加载和启动仍重新执行 live trace。

实现使用站立 hull `mins=(-16,-16,1)`、`maxs=(16,16,72)`；向下 world trace 检查地面与 normal Z，world-only LOS 指向 Bot 胸口。capability 缺失、异常或 native trace 返回 false 都按失败处理，`save/load/start` 会拒绝。上游基线 commit：`616e169a2cc65cd8dcdcc4c5569b5e887f36cd52`（2026-07-18）。

## 5. M0 通过条件

- Camera 与 BotController 均有成功和故障清理证据。
- ABI、CSS/CS2 版本与 PlayerRunCommand hook 有明确记录。
- 世界探针能在已知畅通/阻挡/斜坡/实体内测试点给出正确结果。
- 许可证路径选择 AGPL-3.0 开源，或附上单独商业许可负责人和凭证。
- 形成 `docs/evidence/<server-build>/` 记录后，才允许去掉 save/start 的 fail-closed 门禁。
