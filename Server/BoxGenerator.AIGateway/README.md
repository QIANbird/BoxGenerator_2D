# Box Generator AI Gateway

该服务是 Unity 与阿里云万相之间的安全边界。Unity 只调用本地 Gateway，
云端 API Key 仅由 Gateway 进程读取，不会进入 Unity 工程、场景或安装包。

## 支持的运行模式

`AI_GATEWAY_MODE` 支持三种值：

- `mock`：默认值，不调用云端，返回 Editing 基本型图以测试完整客户端流程。
- `tokenplan`：个人 Token Plan 套餐模式，使用套餐固定域名及 `sk-sp-` Key。
- `wan`：标准百炼 Workspace 模式，保留供以后切换。

启动命令：

```powershell
dotnet run --no-launch-profile --project Server\BoxGenerator.AIGateway\BoxGenerator.AIGateway.csproj
```

默认监听 `http://127.0.0.1:5088`，健康检查地址为：

```text
GET http://127.0.0.1:5088/health
```

## Token Plan 本地测试配置

只在 Windows 用户环境变量或当前终端中设置下列值，不要将真实值写入仓库：

| 环境变量 | 必需 | 说明 |
| --- | --- | --- |
| `AI_GATEWAY_MODE` | 是 | 设置为 `tokenplan` |
| `TOKEN_PLAN_API_KEY` | 是 | Token Plan 控制台签发的 `sk-sp-` Key |
| `AI_IMAGE_OUTPUT_SIZE` | 否 | `1K`（默认）或 `2K` |
| `ASPNETCORE_URLS` | 否 | 默认 `http://127.0.0.1:5088` |

Token Plan 不读取、也不需要 `BAILIAN_WORKSPACE_ID`。其服务地址固定为：

```text
https://token-plan.cn-beijing.maas.aliyuncs.com/api/v1/
```

配置环境变量后，必须关闭原来的终端，新开 PowerShell，再启动 Gateway。
健康检查的预期结果类似：

```json
{
  "status": "ok",
  "mode": "tokenplan",
  "providerConfigured": true,
  "provider": "aliyun-token-plan",
  "model": "wan2.7-image-pro"
}
```

如果 `providerConfigured` 为 `false`，请检查 Key 是否存在且以 `sk-sp-` 开头。
健康检查不会返回 Key 内容。

## 标准百炼 Workspace 模式

如以后需要切回标准模式，配置：

| 环境变量 | 必需 | 说明 |
| --- | --- | --- |
| `AI_GATEWAY_MODE` | 是 | 设置为 `wan` |
| `DASHSCOPE_API_KEY` | 是 | 标准百炼 API Key |
| `BAILIAN_WORKSPACE_ID` | 是 | 北京地域 Workspace ID |
| `BAILIAN_REGION` | 否 | 当前只允许 `cn-beijing` |
| `AI_IMAGE_OUTPUT_SIZE` | 否 | `1K`（默认）或 `2K` |

`BAILIAN_OUTPUT_SIZE` 仍作为旧配置兼容项，但新配置推荐使用
`AI_IMAGE_OUTPUT_SIZE`。

## 调用流程

- `POST /api/v1/generations`：multipart 创建任务。
- `GET /api/v1/generations/{requestId}`：查询任务。
- `GET /api/v1/generations/{requestId}/result`：下载 Gateway 缓存的结果。
- `DELETE /api/v1/generations/{requestId}`：幂等取消客户端任务。

Token Plan 的上游图片接口是同步接口。Gateway 会把调用放入后台单任务队列，
因此 Unity 仍然立即取得任务响应并沿用现有轮询、加载弹窗和取消逻辑。
取消会终止 Gateway 当前等待的 HTTP 请求，并使迟到结果失效；上游已开始的计算
是否停止以及是否计费，仍由阿里云服务端行为决定。

Gateway 固定模型为 `wan2.7-image-pro`，固定 `n=1`，不允许 Unity 指定模型
或生成数量。用户风格图先传，Editing 宝箱图后传，用最后一张输入图保持宝箱
画布比例。结果只在 Gateway 内存中临时缓存，一小时后自动清理。

## 密钥安全

- 不要把 Key 粘贴到聊天、Inspector、场景、脚本、配置文件或 Git。
- 本地开发使用 Windows 用户环境变量；部署时使用平台 Secret 或 KMS。
- Gateway 日志不输出 Authorization、图片 Base64 或完整提示词。
- Unity 安装包只包含 Gateway 地址，不包含任何云端凭证。

当前服务默认只用于本机回环地址。对外部署前还需增加 HTTPS、用户认证、
频率和并发限制、额度告警、请求体限制及正式结果存储访问控制。
