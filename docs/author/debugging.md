# 校验和调试

`project.validate` 的 layer 可为 files、configuration、dependencies、runtime（默认），按顺序执行到该层。诊断包含代码、可确定的文件及 JSON 行列；checks 列出实际执行范围。source 标记固定输入，currentAtCompletion 表明完成时是否仍是当前内容。验证不保存工程。

`run.cpu` 的 seed 默认 1、count 默认 1（最多 100）、limit 默认 500（最多 100000）；每个种子重新运行并回放，报告终局、步数、故障、状态哈希和一致性。未终局、超限或脚本故障返回失败。Ctrl+C 取消并清理会话和 Lua worker。

调试会话是独立文件，包含原始输入版本、种子和明确 ID。用 `debug.start` 的 target 选择新文件；`debug.inspect` 的 session 指向该文件，返回合法动作及当前席位投影；`authority:true` 才额外输出作者权威真值。该文件和权威日志不作为受限玩家记录分发。

`debug.step` 要求 session、期望 step 和 action ID，可指定 seat；重复请求 ID 不执行第二次动作。`debug.record` 要求 session 和新 target，写入正式录制并校验重放。会话期间直接改工程文件不会改变已建立的会话，重新 debug.start 才采用新输入。

所有操作参数以 `describe <ID>` 为当前版本依据。带 source 的成功结果只证明所列版本；不要把旧会话结果当成新磁盘工程的验证。
