# 架构与状态约束

## 分层

- `AwperTrainer.Core`：无游戏依赖的数据模型、地图策略、配置仓库、验证器、运动投影、确定性随机和训练状态机。
- `AwperTrainer.Plugin`：CounterStrikeSharp 命令、Pawn 捕获/传送、camera/ghost、RayTrace 探针、Bot 创建、BotController usercmd 与生命周期清理。
- `BotControllerApi.Contract`：仅用于编译的上游合同子集，程序集名保持 `BotControllerApi`；部署时由上游共享 DLL 替代，且本项目不会复制它到插件输出。
- `RayTraceApi.Contract`：仅用于编译的上游公开合同，程序集名保持 `RayTraceApi`；部署时由 Ray-Trace 上游共享 DLL 替代。
- `AwperTrainer.Core.Tests`：纯本地行为测试，不宣称覆盖 CS2 实体语义。

## Fail-closed 规则

1. 地图不在白名单时拒绝 setup。
2. 配置名或地图名可能越界时在生成路径前拒绝。
3. schemaVersion 或地图不匹配时拒绝加载。
4. RayTrace capability、native trace、Hull/ground/path 任一不可用时拒绝保存、加载和启动。
5. Bot capability 缺失、ABI 不等于 19 或当前会话未成功启用 camera 时拒绝启动。
6. 不存在 Teleport 运动降级路径；Bot 运动仅通过 token 化 usercmd 接口。
7. 所有退出路径必须依次停止 movement、取消 suppression、解除 lock、关闭 camera、恢复玩家。

## 状态机

`IdleReady → Prepare → Countdown → RandomDelay → BotMoving ↔ EndpointPause → Finish → Reset → IdleReady`

- 同一实例只能从 `IdleReady` 启动。
- finish reason 只在第一次结束时写入；Finish/Reset 状态不会再次处理 Bot 死亡。
- direct route 只有 `BotEnd`；jiggle route 严格为 `(BotJiggle, BotStart) × N, BotEnd`。
- seed 使用项目自带 SplitMix64 派生器，避免依赖 `System.Random` 实现变化。
- timeout 从正式开始运动时计算，并覆盖端点停顿。
- 插件运行时为每轮分配递增 generation；异步 Bot 创建和所有 tick 操作只接受当前会话对象，清理采用幂等标志防止重复移除与 token 回收。
