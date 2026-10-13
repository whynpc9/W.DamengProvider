# 当前窗口：W.DamengProvider NuGet 发布准备（2026-10-13）

用户授权：项目与 NuGet 包改名、清理研究材料、tag 触发发布配置、发布前检查及必要修补。本轮不创建或推送发布 tag。

| owner | 角色与唯一写入范围 |
| --- | --- |
| root | 设计、审查、根 README/THIRD-PARTY-NOTICES、发布指南与报告、当前 ownership、GitHub 仓库描述 |
| release_code（Sol High） | src/tests/活跃 tools/eng/.github/global.json/.gitignore；产品身份、发布工具与测试 |
| cleanup（Sol High） | 研究材料与历史生成制品归档清理、保留产品 docs 的当前说明；指定旧研究工具与 T01–T03 入口 |
| verify（Sol Low） | ignored 验证结果；源码停写后唯一 build/test/DB 窗口，不改实现或断言 |
| release_review（Astra High） | 重要发布配置与代码的独立只读审查 |

编码者不执行 dotnet/DB；通知停写后由 Low 串行验证。同一实际新包执行离线安装与 shared/TLS TEST smoke，数据库账号与清理限制仍遵守根 AGENTS.md。任何修补先通知验证者，重跑受影响检查。

历史材料按原内容保存在 ignored `.local/release-prep-20261013/archive/`；Git 历史不重写。本次验证不继承历史候选的通过结论，不重新认证无关历史 evidence。

本轮收口：High 全部停写，Low 构建/实库窗口已释放。十二套离线2050项通过；最终 v2 包消费者、七项首次初始化及 shared/TLS TEST smoke 通过，精确清理闭合。root 已回读最终包/DLL哈希与实库结果。仅完成本地发布准备，用户已授权提交推送当前分支，未tag/上传；详见 [发布前检查](../release-preflight.md)。
