# Box Generator AI Gateway

该服务是 Unity 与阿里云百炼之间的安全边界。Unity 只调用 Gateway，
`DASHSCOPE_API_KEY` 仅由 Gateway 进程读取，不会进入 Unity 工程、场景或安装包。

## 本地 Mock 联调

Gateway 默认是 `mock` 模式，不需要 API Key。启动：

```powershell
dotnet run --project Server\BoxGenerator.AIGateway\BoxGenerator.AIGateway.csproj
```

默认监听 `http://127.0.0.1:5088`。健康检查：

```text
GET http://127.0.0.1:5088/health
```

Unity 场景 `BoxGenerator3D` 已配置 `RemoteAITextureGenerationService`，
地址为 `http://127.0.0.1:5088`。Mock Gateway 会把 Editing 基本型图作为结果返回，
用于验证完整网络链路、加载弹窗、取消、二维展示和下载功能。

## 切换至万相 2.7

请在运行 Gateway 的系统环境中配置以下变量，不要将值写入项目文件：

| 环境变量 | 必需 | 说明 |
| --- | --- | --- |
| `AI_GATEWAY_MODE` | 是 | 设置为 `wan` 才会产生真实云端调用 |
| `DASHSCOPE_API_KEY` | 是 | 北京地域百炼 API Key |
| `BAILIAN_WORKSPACE_ID` | 是 | 北京地域 Workspace ID |
| `BAILIAN_REGION` | 否 | 默认且当前仅允许 `cn-beijing` |
| `BAILIAN_OUTPUT_SIZE` | 否 | `1K`（默认）或 `2K` |
| `ASPNETCORE_URLS` | 否 | 默认 `http://127.0.0.1:5088` |

配置完成后重新启动 Gateway。通过 `/health` 检查：

```json
{
  "status": "ok",
  "mode": "wan",
  "providerConfigured": true,
  "model": "wan2.7-image-pro"
}
```

不要把 API Key 粘贴到聊天、Inspector、命令行脚本或仓库文件中。推荐通过
Windows“环境变量”界面配置开发机的用户级变量；生产环境改用部署平台 Secret
或阿里云 KMS。

## 接口

- `POST /api/v1/generations`：multipart 创建异步任务。
- `GET /api/v1/generations/{requestId}`：查询任务。
- `GET /api/v1/generations/{requestId}/result`：下载已缓存的结果。
- `DELETE /api/v1/generations/{requestId}`：幂等取消客户端任务。

Gateway 固定模型为 `wan2.7-image-pro`、固定 `n=1`，不允许 Unity 指定模型或
生成数量。用户风格图先传，Editing 宝箱图最后传，使万相按最后一张输入图保持
宝箱画布比例。模型图片会在临时内存中缓存一小时，之后自动清理。

当前官方 HTTP 文档没有公开任务取消接口，因此 `DELETE` 会立即停止 Unity
等待并令迟到结果失效，但不保证已经提交给万相的计算和计费会终止。

## 生产部署前

当前默认仅适用于本机回环地址。对外部署时还需要：

- HTTPS；
- 正式用户身份认证，不能依赖 CORS 作为安全边界；
- 用户/IP 频率限制、并发限制和额度告警；
- 开发与生产使用不同 Workspace 和 API Key；
- 反向代理请求体限制；
- 不记录 Authorization、提示词正文、图片或 Base64；
- 结果存储的访问控制和自动过期。
