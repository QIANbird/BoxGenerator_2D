# AI Gateway 接入说明

## 当前实现

Unity 新增 `RemoteAITextureGenerationService`，通过现有
`IAITextureGenerationService` 接口接入，不修改 Coordinator 的生成状态机。

请求链路：

1. Unity 捕获当前 Editing 宝箱画面。
2. Unity 将基本型图与可选风格参考图铺白底并编码为 JPEG，移除透明通道。
3. Unity 以 multipart 提交到本地 Gateway。
4. Gateway 在 `mock` 模式直接返回基本型图；在 `wan` 模式创建百炼异步任务。
5. Unity 轮询 Gateway；Gateway 轮询万相并及时下载临时结果。
6. Unity 将结果规范化为请求画布的精确宽高，再交给现有二维结果展示流程。

## Inspector

`BoxGenerator3D` 场景的 `UIRoot` 已挂载：

- `LocalMockAITextureGenerationService`：保留用于完全离线测试；
- `RemoteAITextureGenerationService`：当前 Coordinator 使用的网络服务。

`RemoteAITextureGenerationService` 可调整：

- `Gateway Base Url`
- `Poll Interval Seconds`
- 单次 HTTP 超时
- 总生成超时
- 输入 JPEG 质量
- 非敏感生命周期日志

Inspector 中不提供也不会读取阿里云 API Key。

如果需要回到完全离线、无需启动 Gateway 的旧测试方式，将
`AITextureGenerationCoordinator.serviceBehaviour` 重新指定为
`LocalMockAITextureGenerationService` 即可。

## 取消语义

用户点击加载弹窗中的 Cancel 后：

- Coordinator 立即进入 Cancelled；
- Unity 中止当前 HTTP 请求；
- Unity 向 Gateway 发送幂等 DELETE；
- Gateway 标记任务取消并拒绝保存迟到结果；
- 已有有效结果和用户输入保持不变。

真实万相任务是否停止取决于服务端是否提供任务取消接口。当前 HTTP 接入只保证
本应用不再等待或展示迟到结果。

## 安全边界

Unity 安装包中只有 Gateway 地址。以下内容只存在于 Gateway 运行环境：

- `DASHSCOPE_API_KEY`
- `BAILIAN_WORKSPACE_ID`
- 地域和模型输出规格

Gateway 日志不输出图片、Base64、完整提示词或 Authorization 请求头。
