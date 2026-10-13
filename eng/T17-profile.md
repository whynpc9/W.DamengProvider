# T17 旧 profile 子门已退役

`eng/t17-profile.sh` 原只消费固定 T16 包 `0.1.0-r3.t16.20261002143244`，依赖该候选的 source manifest 与包/DLL 身份。历史 manifest 已不在当前仓库中；不应将此入口用于当前包验收，也不通过恢复旧 manifest、改包名或伪造新包身份继续运行。

该 runner 现对所有调用输出机器可读 JSON（`task: T17-profile`、`status: retired`、`classification: legacy_profile_retired`、`exit_code: 69`）并以 69 退出。它在参数处理、文件访问、构建、secrets 和数据库操作之前停止。旧可执行消费者已移除，保留的 [R3LobProfileProbe 说明](../tools/R3LobProfileProbe/README.md) 记录退役边界；旧实现可通过 Git 历史查询。

当前公开 stream API 的验收入口是 [eng/t17.sh](t17.sh)，对应 [R3LobProbe](../tools/R3LobProbe/README.md)。它不是旧 raw profile 协议观察的等价替代；本次变更没有运行当前包或数据库验收，也不表示完整 T17 通过。旧 profile 的服务器/codec/opaque server units 观察仍仅是历史 characterization，不能提升为当前包能力结论。
