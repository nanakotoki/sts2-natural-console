# 自然语言控制台 Natural Console

《杀戮尖塔2》(Slay the Spire 2) 模组：用自然语言（中文为主）输入即可执行控制台指令，无需记住指令格式。

## 功能

- **独立输入框**：游戏中按 `F8` 呼出聊天式输入框，输入自然语言后回车即可。
- **本地规则解析（默认，离线）**：中文意图识别 + 中文数字解析 + 卡牌/遗物/药水名称模糊匹配。
- **可选 LLM 增强**：配置 OpenAI 兼容接口后，复杂句子会自动交给大模型翻译（失败自动回退本地规则）。
- **免控制台**：模组自建了一个允许调试指令的 `DevConsole`，无需修改 `settings.save`、无需解锁完整控制台。
- **历史指令**：输入框内按 `↑`/`↓` 浏览历史输入。
- **名称自动补全**：输入卡牌/遗物/药水/事件/状态/敌人名称时按 `Tab` 补全。
- **帮助**：输入 `帮助` 或 `help` 查看系统化的功能清单。
- **角色感知**：添加「打击」「防御」等每个角色都有的初始牌时，优先当前角色的版本。
- **药水俗名**：支持按效果命名的「<buff 名>药水」，如「荆棘药水」→ `流动铜液`、「力量药水」等。
- **歧义处理**：名字有歧义时列出选项，输入序号选择——包括同类同名（如「饥饿」）和跨类别同名（如「暗影步」既是卡牌又是状态）。

## 用法示例

| 输入 | 实际执行 |
|---|---|
| 给我999金币 | `gold 999` |
| 回满血 / 治疗 30 | `heal 999999` / `heal 30` |
| 加一张打击 / 卡牌 打击 | `card STRIKE` |
| 加5张打击 / 给我999张打击 | 加 N 张（会询问放到哪个牌堆） |
| 加5张打击到弃牌堆 | 加 N 张到指定牌堆（手牌/牌组/抽牌堆/弃牌堆/消耗牌堆） |
| 加一张升级的打击 / 升级神化 / 神化+ | 加升级版卡牌（`神化+`） |
| 加5张神化+ / 5张升级的神化 | 加 N 张升级版卡牌 |
| 遗物 灯笼 | `relic LANTERN` |
| 移除遗物 灯笼 | `relic remove LANTERN` |
| 药水 熵增药剂 / 药水 荆棘药水 | `potion ENTROPIC_BREW` / `potion LIQUID_BRONZE` |
| 状态 荆棘 / 状态 力量 3 | `power THORNS_POWER 1 0` / `power STRENGTH_POWER 3 0` |
| 升级第一张牌 / 升级最左边 | `upgrade 0` |
| 移除 打击 | `remove_card STRIKE` |
| 无敌 / 上帝模式 | `godmode` |
| 秒杀 / 直接获胜 | `win` |
| 杀死全部敌人 | `kill all` |
| 去第三幕 | `act 3` |
| 解锁全部 | `unlock all` |
| 自由移动 | `travel` |

## 安装

1. 确保已安装 [BaseLib](https://github.com/Alchyr/BaseLib-StS2)（把 `BaseLib.dll/.json/.pck` 放进 `Slay the Spire 2/mods/BaseLib/`）。
2. 把本模组 `NaturalConsole.dll` 和 `NaturalConsole.json` 放进 `Slay the Spire 2/mods/NaturalConsole/`。
3. 启动游戏，进入一局对局后按 `F8`。

## 可选：配置 LLM（大模型）

在用户数据目录（一般是 `%APPDATA%\SlayTheSpire2`）下创建 `NaturalConsole/config.json`。支持 OpenAI 兼容接口（OpenAI / DeepSeek / Kimi / 通义千问 / 智谱 / Groq / OpenRouter / 本地 Ollama 等）和 Anthropic Claude。

**OpenAI 兼容（默认）：**

```json
{
  "endpoint": "https://api.openai.com/v1",
  "apiKey": "sk-你的密钥",
  "model": "gpt-4o-mini",
  "enabled": true
}
```

**国内模型示例**（都是 OpenAI 兼容，只改 endpoint/model）：

| 服务 | endpoint | model |
|---|---|---|
| DeepSeek | `https://api.deepseek.com/v1` | `deepseek-chat` |
| Kimi (Moonshot) | `https://api.moonshot.cn/v1` | `moonshot-v1-8k` |
| 通义千问 (DashScope) | `https://dashscope.aliyuncs.com/compatible-mode/v1` | `qwen-plus` |
| 智谱 GLM | `https://open.bigmodel.cn/api/paas/v4` | `glm-4-flash` |

**本地 Ollama（无需 API Key）：**

```json
{
  "endpoint": "http://localhost:11434/v1",
  "model": "qwen2.5:7b",
  "enabled": true
}
```

**Anthropic Claude（非 OpenAI 兼容，用 provider 区分）：**

```json
{
  "endpoint": "https://api.anthropic.com",
  "apiKey": "sk-ant-你的密钥",
  "model": "claude-sonnet-4-5",
  "provider": "claude",
  "enabled": true
}
```

不配置该文件则纯本地规则解析，完全离线可用。任何一次 LLM 请求失败都会自动回退到本地规则，不影响使用。

## 开发

- 需要 .NET SDK 9 + Godot 4.5.1（.NET 版，仅在需要生成 `.pck` 资源时用到）。
- `dotnet build` 编译并把 `.dll/.json` 部署到游戏 `mods` 目录。
- 本模组为纯 C# 实现，无需 `.pck`（`has_pck: false`）。
- 首次克隆后需自行创建 `Directory.Build.props`（参考模板，用于设置 Godot 路径）；游戏安装路径会自动发现，无需配置。

## 目录结构

```
NaturalConsoleCode/
  MainFile.cs            模组入口 ([ModInitializer])
  ConsoleExecutor.cs     执行层：自建 DevConsole 并执行指令
  EntityIndex.cs         卡牌/遗物/药水等 → SCREAMING_SNAKE_CASE ID 匹配
  NaturalLanguageParser.cs 本地规则解析器（中文意图/数字/实体）
  LlmTranslator.cs       可选 LLM 增强层（OpenAI 兼容接口）
  UiController.cs        F8 呼出的独立输入框 (Godot, 纯代码构建)
```
