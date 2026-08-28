using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;

namespace NaturalConsole.NaturalConsoleCode;

/// <summary>
/// Optional LLM fallback. When the user configures an endpoint, complex sentences the rule parser
/// can't handle are sent to the model and translated into a console command.
///
/// Supports two request formats:
///   - "openai" (default): OpenAI-compatible <c>/chat/completions</c>. Works with OpenAI, DeepSeek,
///     Kimi/Moonshot, Qwen, Zhipu, Groq, OpenRouter, and local Ollama (no API key needed).
///   - "claude": Anthropic <c>/v1/messages</c>.
///
/// Every failure degrades gracefully back to the local rule parser.
/// </summary>
public static class LlmTranslator
{
    private const string ConfigRelPath = "NaturalConsole/config.json";

    private sealed record LlmSettings(string Endpoint, string ApiKey, string Model, string Provider, bool Enabled);

    private static LlmSettings? _settings;
    private static bool _configLoaded;
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static LlmSettings? Settings
    {
        get
        {
            if (!_configLoaded)
            {
                _configLoaded = true;
                _settings = LoadConfig();
            }

            return _settings;
        }
    }

    public static bool IsEnabled => Settings is { Enabled: true } && !string.IsNullOrWhiteSpace(Settings.Endpoint);

    public static string? TryTranslate(string input)
    {
        LlmSettings? cfg = Settings;
        if (cfg == null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.Endpoint))
            return null;

        try
        {
            string response = CallEndpoint(cfg, input);
            string command = ExtractCommand(response);
            return IsValidCommand(command) ? command : null;
        }
        catch
        {
            return null;
        }
    }

    private static string CallEndpoint(LlmSettings cfg, string input)
    {
        if (cfg.Provider == "claude")
        {
            return CallClaude(cfg, input);
        }

        return CallOpenAiCompatible(cfg, input);
    }

    private static string CallOpenAiCompatible(LlmSettings cfg, string input)
    {
        string endpoint = (cfg.Endpoint ?? "").Trim().TrimEnd('/') + "/chat/completions";
        var body = new
        {
            model = cfg.Model,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt() },
                new { role = "user", content = input },
            },
            temperature = 0.0,
        };

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        // Some local servers (Ollama / LM Studio) don't need auth; skip the header when no key is set.
        if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + cfg.ApiKey);
        }

        req.Content = content;

        using HttpResponseMessage resp = Http.Send(req);
        string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    private static string CallClaude(LlmSettings cfg, string input)
    {
        string baseUrl = (cfg.Endpoint ?? "").Trim().TrimEnd('/');
        if (!baseUrl.EndsWith("/v1", StringComparison.Ordinal))
        {
            baseUrl += "/v1";
        }

        var body = new
        {
            model = cfg.Model,
            max_tokens = 1024,
            system = SystemPrompt(),
            messages = new object[]
            {
                new { role = "user", content = input },
            },
        };

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/messages");
        req.Headers.TryAddWithoutValidation("x-api-key", cfg.ApiKey ?? "");
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        req.Content = content;

        using HttpResponseMessage resp = Http.Send(req);
        string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    private static string ExtractCommand(string response)
    {
        string t = response.Trim().Replace("```", "").Trim();
        if (t.StartsWith("bash") || t.StartsWith("sh") || t.StartsWith("shell"))
        {
            int i = t.IndexOf('\n');
            t = i >= 0 ? t.Substring(i + 1).Trim() : t;
        }

        int nl = t.IndexOf('\n');
        if (nl >= 0)
            t = t.Substring(0, nl);
        return t.Trim();
    }

    private static bool IsValidCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;
        string name = command.Trim().Split(' ')[0].ToLowerInvariant();
        foreach (string known in KnownCommandNames())
        {
            if (known == name)
                return true;
        }

        return false;
    }

    private static string SystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是《杀戮尖塔2》的控制台指令翻译器。把用户的中文自然语言请求翻译成一条控制台指令。");
        sb.AppendLine("只输出指令本身，不要解释，不要标点，不要代码块。");
        sb.AppendLine("卡牌/遗物/药水/事件等 ID 一律使用全大写下划线格式（SCREAMING_SNAKE_CASE）。");
        sb.AppendLine();
        sb.AppendLine("可用指令：");
        foreach (string line in CommandCatalog())
            sb.AppendLine("  " + line);
        return sb.ToString();
    }

    private static List<string> _commandCatalog = null!;
    private static List<string> _commandNames = null!;

    private static List<string> CommandCatalog()
    {
        if (_commandCatalog != null)
            return _commandCatalog;
        var catalog = new List<string>();
        var names = new List<string>();
        try
        {
            foreach (Type t in AbstractConsoleCmdSubtypes.All)
            {
                var cmd = (AbstractConsoleCmd)Activator.CreateInstance(t)!;
                catalog.Add($"{cmd.CmdName} {cmd.Args} — {cmd.Description}");
                names.Add(cmd.CmdName.ToLowerInvariant());
            }
        }
        catch
        {
            // ignore
        }

        _commandCatalog = catalog;
        _commandNames = names;
        return catalog;
    }

    private static List<string> KnownCommandNames()
    {
        _ = CommandCatalog();
        return _commandNames;
    }

    private static LlmSettings? LoadConfig()
    {
        try
        {
            string path = System.IO.Path.Combine(OS.GetUserDataDir(), ConfigRelPath);
            if (!System.IO.File.Exists(path))
                return null;
            string json = System.IO.File.ReadAllText(path);
            using JsonDocument doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string endpoint = GetStr(root, "endpoint") ?? "https://api.openai.com/v1";
            string apiKey = GetStr(root, "apiKey") ?? "";
            string model = GetStr(root, "model") ?? "gpt-4o-mini";
            string provider = GetStr(root, "provider") ?? "openai";
            bool enabled = root.TryGetProperty("enabled", out JsonElement e) && e.GetBoolean();
            return new LlmSettings(endpoint, apiKey, model, provider, enabled);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStr(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }
}