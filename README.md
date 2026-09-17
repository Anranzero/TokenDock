# TokenDock · AI 用量助手

Windows 10/11 x64 **免安装绿色 EXE**。常驻系统托盘，统一展示多个 AI 数据源的**套餐余量**与 **Token 统计**，低余量气泡提醒。当前内置三个页签：

- **OpenCode Go 页**：滚动（5小时）/ 本周 / 本月余量百分比与重置倒计时（官方接口，账号级权威数据），外加独立的「本机 Token 统计」卡（本机会话库只读采集，按客户端分块，**不代表账号总量**）。
- **Codex 页**：通过官方 `codex app-server` 读取 ChatGPT 订阅的额度窗口（按 `windowDurationMins` 动态命名，支持多额度分组与登录授权）。
- **GLM 页**：Z.ai（国际版）/ BigModel（国内版）的 **GLM Coding Plan 账号级远端统计**——5 小时/每周额度与重置倒计时、MCP 额度与工具调用次数、各 GLM 模型 Token 消耗与区间总 Token。数据全部来自远端接口，**不读取本机任何会话数据库**。

使用 C# + WinForms + .NET 8 **自包含单文件发布**：双击运行，无需安装 .NET 运行时、浏览器或 Node.js。

> **改名说明（v2.0.0）**：本程序原名「OpenCode Go 余量助手」，自 v2.0.0 起更名为 **TokenDock · AI 用量助手**（程序集/进程名 `TokenDock`）。首次运行会自动把 `%APPDATA%\OpenCodeGoAssistant` 迁移为 `%APPDATA%\TokenDock`，已保存的 API 密钥与设置平滑延续。

## 界面与外观（v2.1.0）

Windows 11 原生标题栏（固定宽度、禁用最大化，X 关闭即隐藏到托盘），8px 栅格（外边距 24 / 区块间距 16 / 控件高 32），统一 12px 圆角，单一绿色主色（红色仅用于获取失败），状态体系统一（正常 / 即将重置 / 已过期 / 获取失败 / 刷新中，圆点+文字，错误一律内联）。

**外观设置**（设置窗「外观」分组，改动即时生效并落盘，重启保持）：

| 项目 | 取值 |
| --- | --- |
| 主题 | 浅色 / 暗色 / **跟随系统**（读 Windows 个性化设置，60 秒感知一次切换） |
| 窗口效果 | 普通 / **毛玻璃** |
| 透明度 | 60% ~ 100%（默认 88%，下限保证文字可读） |
| 动画 | 开启 / 关闭（关闭后进度条直接到位） |

**毛玻璃的实现方式、兼容范围与降级策略**：整窗半透明（`Form.Opacity`，任何 Windows 版本可用）为基线，叠加 DWM 真模糊——Windows 11 用 Acrylic 亚克力（`SetWindowCompositionAttribute` + `ACCENT_ENABLE_ACRYLICBLURBEHIND`），Windows 10 1709+ 用 Aero BlurBehind；远程会话（RDP）直接跳过模糊避免灰块；系统版本过低、API 调用失败或任何异常一律静默降级为半透明磨砂，不弹错。深色标题栏通过 `DWMWA_USE_IMMERSIVE_DARK_MODE` 同步（Win10 1809+，属性 20→19 自动回退）。毛玻璃模式下卡片边框更细更淡、阴影更轻。

## 快速使用

1. 把 `dist\TokenDock.exe` 复制到任意 Windows 10/11 x64 机器（U 盘拷贝即可，免安装）。
2. 双击运行。首次启动会弹出设置窗口，粘贴对应服务的凭据并「保存并验证」（验证通过才写入本机）：
   - **OpenCode Go**：OpenCode 控制台生成的 API Key。
   - **Codex**：不需要密钥，点 Codex 页的「登录 ChatGPT」走官方授权。
   - **GLM**：GLM 页点「GLM 密钥」，先选 Provider（Z.ai 国际 / BigModel 国内，Base URL 与密钥各自独立），再粘贴该 Provider 的 Coding Plan API Key。
3. 程序缩到系统托盘运行：

| 操作 | 效果 |
| --- | --- |
| 悬停托盘图标 | 显示 5小时 / 周 / 月剩余百分比（图标颜色随余量变化） |
| 左键点击图标 | 打开详情窗口（三个页签：OpenCode Go / Codex / GLM） |
| 右键图标 | 打开详情 / 立即刷新 / 退出（菜单随主题深浅色） |

- 每 **60 秒**自动刷新一次；打开详情窗口、切换页签时也会立即刷新。
- 任一窗口剩余 ≤ **20%** 时弹一次气泡提醒，恢复后重新武装。

## 数据来源与口径（不编造数据）

**OpenCode Go**：`GET https://opencode.ai/zen/go/v1/usage`（Bearer 鉴权）。仅读取真实返回的 `usage.rolling/weekly/monthly` 的 `percent`（已用百分比）、`status`、`resetsAt`；**剩余百分比 = 100 − 已用百分比**，钳制 0~100。程序不显示、也不推断接口未返回的数字，缺一律显示「未知」。

**Codex**：官方 `codex app-server`（stdio JSONL，JSON-RPC 2.0）。`initialize` → `account/read` → `account/rateLimits/read`，并监听 `rateLimits/updated` 通知即时刷新。窗口**按 `windowDurationMins` 动态命名**（300 → 「5小时窗口」、10080 → 「7天窗口」），兼容多额度分组（按 limitId 去重）与字段缺失。令牌由 Codex 本机保管，本程序不读取、不记录、不保存。

**GLM Coding Plan**（v2.2.0 起，字段与路径均实测确认，两套接口族）：

| 内容 | Z.ai（国际版 `api.z.ai`） | BigModel（国内版 `open.bigmodel.cn`） |
| --- | --- | --- |
| 额度（5小时/每周） | `/api/monitor/usage/quota/limit` | 同左（类型为 `CREDIT_LIMIT`，成功码 `code:200`） |
| 每月模型 Token | `/api/monitor/usage/credit-usage/usage-detail?usageType=MODEL` | `/api/monitor/usage/model-usage?type=1&startTime&endTime` |
| MCP 调用明细 | 同上（`usageType=MCP`） | `/api/monitor/usage/tool-usage?type=1&...` |
| MCP 月额度 | `/api/v1/mcp/usage` | 上游未提供（接口 404，界面不显示该卡） |
| 鉴权 | `Authorization: Bearer <key>` + `X-Bigmodel-Authorization: Bearer <key>` | 同左 |

- 额度标签按账号实测的 `(type, unit, number)` 三元组映射：`CREDIT_LIMIT/TOKENS_LIMIT + unit=3 + number=5` → 「5 小时剩余」，`+ unit=6` → 「每周剩余」，`TIME_LIMIT + unit=5 + number=1` → 「工具调用（MCP）」；**接口返回几条渲染几条，未命中映射时显示原始 type，不臆测**。
- 剩余百分比优先用 `剩余/上限`（上限是 `usage` 字段）直算，缺失时用 `percentage` 反推，两者都缺则显示「接口未返回」。
- 模型列表**根据接口动态生成**，不写死模型名；接口提供输入/输出/缓存列的分别展示，**没有的列整列不显示**（BigModel 当前只提供每模型合计 Token，就只显示合计）。

**本机 Token 统计**（OpenCode Go 页底部的独立卡，v2.0.4 起与官方数据彻底分离）：

- 只读扫描本机客户端会话库（OpenCode `~/.local/share/opencode/opencode*.db`；Zcode `~/.zcode/cli/db/db.sqlite` 的 `model_usage` 表），仅取 `provider=opencode-go` 的记录，按消息 ID 去重。
- **按客户端分块展示**来源、记录条数、实际覆盖时间范围与按模型表格；**不跨客户端合并成"账号总量"**；某客户端无记录时明确写「不代表账号用量为 0」。
- 官方接口尚未提供账号级逐请求/逐模型 Token 历史，故不拼接多客户端数据伪造总量；待官方开放 usage history 后再接入。

## 异常处理

| 场景 | 提示 | 行为 |
| --- | --- | --- |
| 断网 / 超时 | 「网络连接失败…」 | 保留旧数据，标记「数据已过期 · 最后成功时间」，绝不显示为满额 |
| 密钥无效（HTTP 401） | 「密钥无效或已失效（HTTP 401）…」并附服务端 message | 同上 |
| 无权限 / 无订阅（HTTP 402/403） | 按服务分类提示 | 同上 |
| 套餐不存在 / 已过期（业务消息含「不存在coding plan」「没有资格」） | 「该账号未订阅 Coding Plan（或套餐已过期）」 | 同上 |
| 限流（HTTP 429） | 「请求过于频繁…」 | 同上 |
| 服务端 5xx / 其他状态码 | 「服务端异常 / 异常状态码…」 | 同上 |
| 返回 200 但格式不符 | 「接口返回格式不符合预期」 | 同上 |

托盘图标变灰、状态胶囊显示「已过期」，均可一眼识别数据已过期。

## 密钥安全

- 所有密钥使用 **Windows DPAPI（当前用户作用域）** 加密后保存在 `%APPDATA%\TokenDock\`：
  `apikey.bin`（OpenCode）、`glm-apikey-zai.bin` / `glm-apikey-bigmodel.bin`（GLM，按 Provider 分开）；只有同一台机器的同一个 Windows 用户能解密。
- 密钥**不写入任何日志**、不上传对应服务接口以外的任何第三方；`--glmcheck` 等自检命令只输出密钥长度，不回显内容。仓库 `.gitignore` 已排除 `*.bin`。

## 构建与测试（开发机需要 .NET 8 SDK）

```powershell
# 构建 + 发布到 dist\
.\build.ps1

# 构建 + 运行单元测试 + 发布
.\build.ps1 -RunTests
```

无 GUI 自检命令：

```powershell
dist\TokenDock.exe --selftest           # 解析/分类/失败保留/DPAPI/主题对比度/设置存取
dist\TokenDock.exe --uipreview          # 三套外观 × 100/125/150% 渲染 PNG 到 %TEMP%\tokendock\uipreview\
dist\TokenDock.exe --appearance-check   # 逐组合应用窗口效果，输出实际模糊档位与对比度
dist\TokenDock.exe --codexcheck         # codex app-server 端到端握手
dist\TokenDock.exe --tokenscheck        # 本机会话库扫描 + 与官方 stats 交叉核对
dist\TokenDock.exe --glmcheck           # GLM 远端接口实测（打印每项字段与原始响应片段，不回显密钥）
```

## 目录结构

```
├── build.ps1 / build.cmd      # 打包脚本
├── dist\TokenDock.exe         # 交付的可运行单文件 EXE（构建产物，不入库）
├── src\TokenDock\
│   ├── Program.cs                     # 入口（自检开关、单实例互斥、外观加载）
│   ├── Models.cs / AppState.cs        # 用量模型、失败分类、状态（失败保留旧数据）
│   ├── UsageApiClient.cs              # OpenCode Go 接口客户端
│   ├── TokenUsage.cs / TokenCheck.cs  # 本机会话库采集与 --tokenscheck
│   ├── CodexModels.cs / CodexProtocol.cs / CodexAppServerClient.cs
│   ├── CodexPanel.cs / CodexCheck.cs  # Codex 页控件与端到端自检
│   ├── GlmUsage.cs                    # GLM 数据层（两家接口族客户端 + 解析 + 状态）
│   ├── GlmPanel.cs                    # GLM 页控件（额度卡 / MCP 卡 / 动态模型表）
│   ├── GlmSettingsForm.cs             # GLM 设置窗（Provider 选择 + 独立密钥）
│   ├── GlmCheck.cs                    # --glmcheck 远端实测
│   ├── DetailForm.cs                  # 主窗口（三页签 + 本机 Token 统计卡）
│   ├── LocalTokensCard.cs             # 本机 Token 统计卡（按客户端分块）
│   ├── SettingsForm.cs                # 设置窗（API 密钥 + 外观）
│   ├── Appearance.cs / AppearanceControls.cs / WindowEffects.cs  # 外观控制、滑杆/开关、窗口模糊
│   ├── UiTheme.cs / ModernControls.cs # 调色板（浅/暗/毛玻璃）与自绘控件
│   ├── SecureKeyStore.cs              # DPAPI 密钥存储（OpenCode + GLM 各 Provider）
│   ├── DisplayFormat.cs               # 中文格式化（剩余%、状态、倒计时）
│   ├── TrayApplicationContext.cs      # 托盘常驻、60s 刷新、低余量提醒
│   ├── TrayIconFactory.cs / WindowPlacement.cs
│   ├── UiPreview.cs / SelfTest.cs     # 界面预览与命令行自检
└── tests\TokenDock.Tests\             # 130 项 xunit 测试
```

## 验证情况（如实说明）

**已验证：**

- `dotnet build` 0 警告 0 错误；**130 项 xunit 测试全部通过**（OpenCode 解析/分类、Codex 协议、GLM 解析与实测回归夹具、外观调色板对比度与设置存取、Token 采集口径等）。
- **OpenCode Go**：真实 API Key 下 `usage.rolling/weekly/monthly` 的 `percent/status/resetsAt` 解析与「剩余 = 100 − 已用」口径与真实接口一致（v1.1.0 起持续使用验证）。
- **Codex**：`--codexcheck` 全通过——检测 CLI → initialize → `account/read`（已登录）→ `account/rateLimits/read` 返回 4 个额度窗口（需 codex-cli ≥ 0.154.0，旧版无法解析 `prolite` 计划）。
- **GLM（BigModel 真机实测，2026-09-17）**：`--glmcheck` 用真实密钥打通 3 个接口——额度 `lite` 套餐（5 小时：上限 2000 / 剩余 1185 / 已用 40% / 23:28 重置；每周：上限 10000 / 剩余 5175 / 已用 48% / 09-24 01:10 重置）、模型 Token（GLM-5.3 3751 万 + GLM-5.3-Flash 7.05 亿，区间总 7.43 亿、总调用 4469 次）、MCP 工具（联网搜索 10 次 + 网页读取 2 次 = 12 次）。
- 外观：`--appearance-check` 在本机 Win11 毛玻璃走到 Acrylic 档、四组合调用无异常；浅/暗两套调色板文字对比度（主文字 ≥ 7、次文字 ≥ 4）全部达标；`--uipreview` 三套外观 × 三档缩放逐张核对无重叠无裁切。
- 发布产物为约 64 MB 自包含单文件，`dist` 下无多余文件；`--selftest` / `--tokenscheck` / `--glmcheck` 在发布产物上全部通过。

**未验证项：**

- **GLM 的 Z.ai（国际版）路径未做端到端实测**（本机只有 BigModel 账号）：其接口路径/字段来自 ZCode 客户端代码确认，鉴权头当前同时携带 `Authorization` 与 `X-Bigmodel-Authorization` 两套方案，待有 Z.ai 账号后收敛。
- GLM 数据与 ZCode「使用统计 → 编程套餐」的**逐项人工对比**尚未完成（数值已在 `--glmcheck` 输出，需人工核对一次）。
- **毛玻璃的真实观感**（模糊强度、与桌面背景的叠加）需人眼在真机确认；自动化只能验证调用成功与实际档位。
- **Win10 实机**未验证（开发机为 Win11）；Windows 10 的 BlurBehind 路径只验证了分支逻辑与不报错。
- Codex 登录**完整闭环**（浏览器授权 → 轮询拿到额度）未端到端走过：`account/login/start` 返回 `authUrl` 已实测，回调需人工完成。
- 本机 Token 统计只覆盖上述两个客户端；其他客户端（Claude Code、Cursor 等）需单独适配，**不假定数据互通**。
- 全新无 .NET 环境机器的双击运行未取机验证（自包含发布理论上已覆盖）。
