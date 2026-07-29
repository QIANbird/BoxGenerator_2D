# AI Gateway 接入说明

## 当前实现

Unity 通过 `RemoteAITextureGenerationService` 接入 Gateway，继续使用既有
`IAITextureGenerationService` 和 Coordinator 状态机。

请求链路：

1. Unity 捕获用户当前视角下的 Editing 宝箱画面。
2. Unity 将基本型图与可选风格参考图铺白底、编码成 JPEG 并移除透明通道。
3. Unity 用 multipart 将提示词和图片提交给本地 Gateway。
4. Gateway 根据 `AI_GATEWAY_MODE` 使用 Mock、Token Plan 或标准百炼 Provider。
5. Token Plan 同步云调用在 Gateway 后台单任务队列中执行；Unity 仍轮询本地任务。
6. Gateway 下载并临时缓存云端结果。
7. Unity 将结果规范化到请求画布的精确宽高，再交给二维结果展示流程。

## Inspector

`BoxGenerator3D` 场景的 `UIRoot` 挂载了：

- `LocalMockAITextureGenerationService`：完全离线测试。
- `RemoteAITextureGenerationService`：当前 Coordinator 使用的网络服务。

`RemoteAITextureGenerationService` 可以调整 Gateway 地址、轮询间隔、HTTP
超时、总生成超时、输入 JPEG 质量和非敏感日志。Inspector 不提供、也不会
读取任何阿里云 API Key。

## Token Plan 与标准百炼

Token Plan 使用：

- `AI_GATEWAY_MODE=tokenplan`
- `TOKEN_PLAN_API_KEY=sk-sp-...`
- 固定北京套餐域名
- 不使用 Workspace ID

标准百炼 Workspace 模式继续使用：

- `AI_GATEWAY_MODE=wan`
- `DASHSCOPE_API_KEY`
- `BAILIAN_WORKSPACE_ID`
- `BAILIAN_REGION=cn-beijing`

两种模式只在 Gateway 内切换，对 Unity 协议和场景引用没有影响。

## 取消语义

用户点击加载弹窗中的 Cancel 后：

- Coordinator 立即进入 Cancelled。
- Unity 中止当前 HTTP 请求并向 Gateway 发送幂等 DELETE。
- Gateway 标记任务取消，并取消正在等待的 Token Plan HTTP 调用。
- 迟到结果不会写入任务，也不会进入二维展示。
- 现有有效结果和用户输入保持不变。

取消不能保证阿里云已经开始的计算或计费一定停止。

## 安全边界

Unity 安装包只有 Gateway 地址。以下内容只存在于 Gateway 运行环境：

- Token Plan 的 `TOKEN_PLAN_API_KEY`，或标准模式的 `DASHSCOPE_API_KEY`。
- 标准模式所需的 `BAILIAN_WORKSPACE_ID`。
- 地域和模型输出规格。

Gateway 不记录 Key、Authorization、图片 Base64 或完整提示词。
