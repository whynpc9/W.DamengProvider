# T17 旧 LOB profile 工具已退役

`R3LobProfileProbe` 原为 `capability_characterization` 观察工具，只消费固定的 T16 包 `0.1.0-r3.t16.20261002143244` 及其历史 source manifest。该 manifest 已不在当前仓库中；此工具不适用于当前 `W.DamengProvider` 包验收，旧可执行消费者已移除。历史实现与观察记录可通过 Git 历史查询，不恢复旧 manifest 或替换包身份。

[旧 runner](../../eng/t17-profile.sh) 现始终输出 `status: retired` 的 JSON 并以 69 退出，在参数处理、文件访问、构建、加载 secrets 或数据库操作之前停止。详细退役说明见 [T17 profile](../../eng/T17-profile.md)。

当前公开 stream API 的验收入口是 [eng/t17.sh](../../eng/t17.sh)，使用 `R3LobProbe` 检查公开 LOB API 的行为。它不是旧 raw profile 协议观察的等价替代；本次退役不证明当前包或完整 T17 验收通过。历史 profile 只描述当时服务器、codec 和 opaque server units 的观察，不代表完整流式 API、绝对 seek 或当前包能力。
