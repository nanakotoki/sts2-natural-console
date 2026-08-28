using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace NaturalConsole.NaturalConsoleCode;

public class Choice
{
    public string Label = "";
    public string Command = "";
}

public class ParseResult
{
    public string? Command;
    public string Error = "";
    public List<Choice>? Choices;

    public static ParseResult Ok(string cmd) => new() { Command = cmd };

    public static ParseResult Fail(string err) => new() { Error = err };

    public static ParseResult Choose(List<Choice> choices) => new() { Choices = choices };
}

/// <summary>
/// Rule-based parser that turns Chinese (and some English) natural-language input into a console
/// command string understood by <see cref="ConsoleExecutor"/>. When a name is ambiguous (several
/// entities share it), returns a list of choices for the UI to ask the user.
/// </summary>
public static class NaturalLanguageParser
{
    public static ParseResult Parse(string raw)
    {
        string s = Normalize(raw);
        if (string.IsNullOrWhiteSpace(s))
        {
            return ParseResult.Fail("请输入指令，例如：给我999金币");
        }

        string? c;
        if ((c = MatchToggle(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchAct(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchUnlock(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchRoom(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchDraw(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchGold(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchHeal(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchEnergy(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchBlock(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchDamage(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchStars(s)) != null) return ParseResult.Ok(c);
        if ((c = MatchKill(s)) != null) return ParseResult.Ok(c);

        ParseResult? r;
        if ((r = MatchRelic(s)) != null) return r;
        if ((r = MatchPotion(s)) != null) return r;
        if ((r = MatchRemoveCard(s)) != null) return r;
        if ((r = MatchUpgradedCard(s)) != null) return r;
        if ((r = MatchUpgrade(s)) != null) return r;
        if ((r = MatchCard(s)) != null) return r;
        if ((r = MatchPower(s)) != null) return r;
        if ((r = MatchFight(s)) != null) return r;
        if ((r = MatchEvent(s)) != null) return r;

        // Cross-kind fallback: a bare name (or one that matches several kinds) needs disambiguation.
        if ((r = MatchCrossKind(s)) != null) return r;

        return ParseResult.Fail(NotFoundMessage(s));
    }

    // ---------------------------------------------------------------------------------------------
    // Intents (non-entity)
    // ---------------------------------------------------------------------------------------------

    private static string? MatchToggle(string s)
    {
        if (ContainsAny(s, "无敌", "上帝模式", "godmode", "开挂", "不死"))
            return "godmode";

        if (ContainsAny(s, "直接获胜", "秒杀", "胜利", "结束战斗", "win") && !ContainsAny(s, "敌人"))
            return "win";

        if (ContainsAny(s, "自杀", "死亡", "让我死", "die"))
            return "die";

        if (ContainsAny(s, "即时模式", "加速", "instant", "快进"))
            return "instant";

        if (ContainsAny(s, "自由移动", "旅行", "travel"))
            return "travel";

        return null;
    }

    private static string? MatchAct(string s)
    {
        if (!ContainsAny(s, "幕", "章节", "act", "去第", "前往", "跳到第"))
            return null;

        long? num = ExtractNumber(s);
        if (num.HasValue)
            return "act " + num.Value;

        string? entry = EntityIndex.Resolve(EntityIndex.Kind.Act, StripActKeywords(s));
        return entry == null ? null : "act " + entry;
    }

    private static string? MatchUnlock(string s)
    {
        if (!ContainsAny(s, "解锁", "unlock"))
            return null;

        if (ContainsAny(s, "全部", "所有", "一切", "all"))
            return "unlock all";

        foreach (string type in new[] { "卡牌", "遗物", "药水", "怪物", "事件", "成就" })
        {
            if (s.Contains(type))
                return "unlock " + type;
        }

        return "unlock all";
    }

    private static string? MatchRoom(string s)
    {
        if (!ContainsAny(s, "房间", "room"))
            return null;

        foreach ((string zh, string type) in new[]
        {
            ("精英", "Elite"), ("首领", "Boss"), ("boss", "Boss"), ("宝箱", "Treasure"),
            ("宝藏", "Treasure"), ("商店", "Shop"), ("事件", "Event"), ("休息", "RestSite"),
            ("篝火", "RestSite"), ("地图", "Map"), ("怪物", "Monster"),
        })
        {
            if (s.Contains(zh))
                return "room " + type;
        }

        return null;
    }

    private static string? MatchGold(string s)
    {
        if (!ContainsAny(s, "金币", "金钱", "钱", "gold", "铜板"))
            return null;

        long? num = ExtractNumber(s);
        if (num.HasValue)
            return "gold " + num.Value;

        return ContainsAny(s, "无限", "很多", "大量") ? "gold 999999" : null;
    }

    private static string? MatchHeal(string s)
    {
        if (!ContainsAny(s, "回血", "加血", "治疗", "回满", "回复生命", "heal", "回满血"))
            return null;

        long? num = ExtractNumber(s);
        if (num.HasValue)
            return "heal " + num.Value;

        if (ContainsAny(s, "满", "全血", "回满"))
            return "heal 999999";

        return null;
    }

    private static string? MatchEnergy(string s)
    {
        if (!ContainsAny(s, "能量", "费用", "energy", "蓝量"))
            return null;

        long? num = ExtractNumber(s);
        return num.HasValue ? "energy " + num.Value : null;
    }

    private static string? MatchBlock(string s)
    {
        if (!ContainsAny(s, "格挡", "护甲", "护盾", "block"))
            return null;

        long? num = ExtractNumber(s);
        return num.HasValue ? "block " + num.Value : null;
    }

    private static string? MatchDamage(string s)
    {
        if (!ContainsAny(s, "伤害", "damage"))
            return null;

        long? num = ExtractNumber(s);
        return num.HasValue ? "damage " + num.Value : null;
    }

    private static string? MatchDraw(string s)
    {
        if (!ContainsAny(s, "抽牌", "抽卡", "draw", "摸牌"))
            return null;

        long? num = ExtractNumber(s);
        return num.HasValue ? "draw " + num.Value : "draw 1";
    }

    private static string? MatchStars(string s)
    {
        if (!ContainsAny(s, "星星", "星数", "stars"))
            return null;

        long? num = ExtractNumber(s);
        return num.HasValue ? "stars " + num.Value : null;
    }

    private static string? MatchKill(string s)
    {
        if (!ContainsAny(s, "杀死", "消灭", "击杀", "秒杀敌人", "kill", "干掉"))
            return null;

        if (ContainsAny(s, "全部", "所有", "all"))
            return "kill all";

        long? num = ExtractNumber(s);
        if (num.HasValue)
            return "kill " + Math.Max(0, num.Value - 1);

        return "kill all";
    }

    // ---------------------------------------------------------------------------------------------
    // Intents (entity, ambiguity-aware)
    // ---------------------------------------------------------------------------------------------

    private static ParseResult? MatchRelic(string s)
    {
        if (!ContainsAny(s, "遗物", "relic", "圣物"))
            return null;

        bool remove = ContainsAny(s, "移除", "删除", "remove", "去掉");
        return ResolveEntity(EntityIndex.Kind.Relic, s, e => (remove ? "relic remove " : "relic ") + e);
    }

    private static ParseResult? MatchPotion(string s)
    {
        if (!ContainsAny(s, "药水", "药剂", "potion"))
            return null;

        return ResolveEntity(EntityIndex.Kind.Potion, s, e => "potion " + e);
    }

    private static ParseResult? MatchCard(string s)
    {
        if (!ContainsAny(s, "加一张", "加张", "给一张", "来一张", "刷一张", "要一张", "添加一张", "加牌", "加卡", "card", "获得卡", "获取", "获得", "卡牌", "拿", "张"))
            return null;

        long? num = ExtractNumber(s);
        int count = num.HasValue ? (int)Math.Max(1, num.Value) : 1;
        string? pile = DetectPile(s);

        List<EntityIndex.EntityMatch> matches = EntityIndex.ResolveCandidates(EntityIndex.Kind.Card, s);
        if (matches.Count == 0)
        {
            return null;
        }

        // Ambiguous card name -> ask which card (use the pile if already specified).
        if (matches.Count > 1)
        {
            var choices = matches
                .Select(m => new Choice { Label = m.Title + "（" + m.Entry + "）", Command = CardCommand(m.Entry, count, pile) })
                .ToList();
            return ParseResult.Choose(choices);
        }

        string entry = matches[0].Entry;

        if (count == 1)
        {
            return ParseResult.Ok(CardCommand(entry, 1, pile));
        }

        // "N张" with no pile specified -> ask where to put them.
        if (pile == null)
        {
            return BuildPileChoices(count, entry);
        }

        return ParseResult.Ok(CardCommand(entry, count, pile));
    }

    private static string CardCommand(string entry, int count, string? pile)
    {
        if (count == 1)
        {
            return pile == null ? "card " + entry : "card " + entry + " " + pile;
        }

        return $"{ConsoleExecutor.AddCardPrefix}{count} {entry} " + (pile ?? "Deck");
    }

    private static ParseResult BuildPileChoices(int count, string entry)
    {
        (string label, string pile)[] piles =
        {
            ("手牌", "Hand"),
            ("牌组", "Deck"),
            ("抽牌堆", "Draw"),
            ("弃牌堆", "Discard"),
            ("消耗牌堆", "Exhaust"),
        };

        var choices = piles
            .Select(p => new Choice { Label = p.label, Command = $"{ConsoleExecutor.AddCardPrefix}{count} {entry} {p.pile}" })
            .ToList();
        return ParseResult.Choose(choices);
    }

    private static string? DetectPile(string s)
    {
        if (ContainsAny(s, "手牌", "手里", "手中"))
            return "Hand";
        if (ContainsAny(s, "牌组", "牌库"))
            return "Deck";
        if (ContainsAny(s, "抽牌堆"))
            return "Draw";
        if (ContainsAny(s, "弃牌堆"))
            return "Discard";
        if (ContainsAny(s, "消耗堆", "消耗牌堆", "耗尽"))
            return "Exhaust";
        return null;
    }

    private static ParseResult? MatchUpgradedCard(string s)
    {
        if (!ContainsAny(s, "升级", "强化", "进阶", "upgraded", "+", "plus"))
            return null;

        long? num = ExtractNumber(s);
        int count = num.HasValue ? (int)Math.Max(1, num.Value) : 1;
        string? pile = DetectPile(s);

        if (count > 1)
        {
            // "5张X+" / "5张升级的X" -> N upgraded cards, optionally to a specific pile.
            return ResolveEntity(EntityIndex.Kind.Card, s, e =>
                $"{ConsoleExecutor.AddUpgradedCardPrefix}{count} {e}" + (pile != null ? " " + pile : ""));
        }

        return ResolveEntity(EntityIndex.Kind.Card, s, e => ConsoleExecutor.UpgradedCardPrefix + e);
    }

    private static ParseResult? MatchRemoveCard(string s)
    {
        if (!ContainsAny(s, "移除", "删除", "删掉", "去掉"))
            return null;

        if (!ContainsAny(s, "牌", "卡", "card"))
            return null;

        return ResolveEntity(EntityIndex.Kind.Card, s, e => "remove_card " + e);
    }

    private static ParseResult? MatchUpgrade(string s)
    {
        if (!ContainsAny(s, "升级", "upgrade"))
            return null;

        long? num = ExtractNumber(s);
        if (num.HasValue)
            return ParseResult.Ok("upgrade " + Math.Max(0, num.Value - 1));

        if (ContainsAny(s, "最左", "第一张", "第一"))
            return ParseResult.Ok("upgrade 0");

        if (ContainsAny(s, "最右", "最后一张", "最后"))
        {
            CardPile? hand = GetHand();
            int count = hand?.Cards.Count ?? 1;
            return ParseResult.Ok("upgrade " + Math.Max(0, count - 1));
        }

        return null;
    }

    private static ParseResult? MatchPower(string s)
    {
        if (!ContainsAny(s, "状态", "power", "buff", "增益", "层"))
            return null;

        long? num = ExtractNumber(s);
        string amount = num?.ToString() ?? "1";
        return ResolveEntity(EntityIndex.Kind.Power, s, e => $"power {e} {amount} 0");
    }

    private static ParseResult? MatchFight(string s)
    {
        if (!ContainsAny(s, "战斗", "fight", "遭遇战", "打boss"))
            return null;

        return ResolveEntity(EntityIndex.Kind.Monster, s, e => "fight " + e);
    }

    private static ParseResult? MatchEvent(string s)
    {
        if (!ContainsAny(s, "事件", "event"))
            return null;

        return ResolveEntity(EntityIndex.Kind.Event, s, e => "event " + e);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a name against every entity kind at once. When the name exists in several kinds
    /// (e.g. 暗影步 is both a card and a power), returns a choice list so the user can pick.
    /// </summary>
    private static ParseResult? MatchCrossKind(string s)
    {
        long? num = ExtractNumber(s);
        string amount = num?.ToString() ?? "1";

        (EntityIndex.Kind kind, string label, Func<string, string> cmd)[] kinds =
        {
            (EntityIndex.Kind.Card, "卡牌", e => "card " + e),
            (EntityIndex.Kind.Power, "状态", e => $"power {e} {amount} 0"),
            (EntityIndex.Kind.Relic, "遗物", e => "relic " + e),
            (EntityIndex.Kind.Potion, "药水", e => "potion " + e),
            (EntityIndex.Kind.Event, "事件", e => "event " + e),
            (EntityIndex.Kind.Monster, "敌人", e => "fight " + e),
        };

        var found = kinds
            .Select(k => (k.kind, k.label, k.cmd, matches: EntityIndex.ResolveCandidates(k.kind, s)))
            .Where(x => x.matches.Count > 0)
            .ToList();

        if (found.Count == 0)
        {
            return null;
        }

        var choices = new List<Choice>();
        foreach (var (kind, label, cmd, matches) in found)
        {
            foreach (var m in matches)
            {
                choices.Add(new Choice { Label = label + " " + m.Title + "（" + m.Entry + "）", Command = cmd(m.Entry) });
            }
        }

        if (choices.Count == 1)
        {
            return ParseResult.Ok(choices[0].Command);
        }

        return ParseResult.Choose(choices);
    }

    private static ParseResult? ResolveEntity(EntityIndex.Kind kind, string s, Func<string, string> commandFor)
    {
        List<EntityIndex.EntityMatch> matches = EntityIndex.ResolveCandidates(kind, s);
        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count == 1)
        {
            return ParseResult.Ok(commandFor(matches[0].Entry));
        }

        var choices = matches
            .Select(m => new Choice { Label = m.Title + "（" + m.Entry + "）", Command = commandFor(m.Entry) })
            .ToList();
        return ParseResult.Choose(choices);
    }

    private static string NotFoundMessage(string s)
    {
        var sb = new StringBuilder("没听懂。");
        if (ContainsAny(s, "药水", "药剂", "potion"))
            AppendSuggestion(sb, "药水", EntityIndex.Kind.Potion, s);
        if (ContainsAny(s, "遗物", "relic", "圣物"))
            AppendSuggestion(sb, "遗物", EntityIndex.Kind.Relic, s);
        if (ContainsAny(s, "卡牌", "牌", "卡", "card", "一张"))
            AppendSuggestion(sb, "卡牌", EntityIndex.Kind.Card, s);
        if (ContainsAny(s, "状态", "power", "buff", "增益", "层"))
            AppendSuggestion(sb, "状态", EntityIndex.Kind.Power, s);
        if (ContainsAny(s, "事件", "event"))
            AppendSuggestion(sb, "事件", EntityIndex.Kind.Event, s);
        if (ContainsAny(s, "敌人", "怪物", "战斗", "fight"))
            AppendSuggestion(sb, "敌人", EntityIndex.Kind.Monster, s);

        sb.Append("\n试试：给我999金币 / 回满血 / 加一张打击 / 无敌 / 秒杀 / 去第三幕");
        return sb.ToString();
    }

    private static void AppendSuggestion(StringBuilder sb, string label, EntityIndex.Kind kind, string s)
    {
        var sugg = EntityIndex.Suggest(kind, s, 5);
        if (sugg.Count > 0)
        {
            sb.Append("\n  ").Append(label).Append("候选：").Append(string.Join(" / ", sugg));
        }
    }

    private static string StripActKeywords(string s)
    {
        return s.Replace("去第", "").Replace("前往", "").Replace("跳到第", "").Replace("幕", "").Replace("章节", "").Trim();
    }

    private static CardPile? GetHand()
    {
        try
        {
            var me = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
            return me == null ? null : PileType.Hand.GetPile(me);
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsAny(string s, params string[] needles)
    {
        foreach (string n in needles)
        {
            if (s.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (ch == '　')
                sb.Append(' ');
            else if (ch >= '！' && ch <= '～')
                sb.Append((char)(ch - 0xFEE0));
            else
                sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static long? ExtractNumber(string s)
    {
        Match m = Regex.Match(s, @"\d+");
        if (m.Success)
            return long.Parse(m.Value);

        string chinese = ChineseDigits + ChineseUnits;
        var sb = new StringBuilder();
        foreach (char ch in s)
        {
            if (chinese.IndexOf(ch) >= 0)
                sb.Append(ch);
            else if (sb.Length > 0)
                break;
        }

        return sb.Length > 0 ? ParseChineseNumber(sb.ToString()) : null;
    }

    private const string ChineseDigits = "零一二三四五六七八九两";
    private const string ChineseUnits = "十百千万亿";

    private static long? ParseChineseNumber(string s)
    {
        var digits = new Dictionary<char, long>
        {
            { '零', 0 }, { '一', 1 }, { '二', 2 }, { '两', 2 }, { '三', 3 }, { '四', 4 },
            { '五', 5 }, { '六', 6 }, { '七', 7 }, { '八', 8 }, { '九', 9 },
        };
        var units = new Dictionary<char, long>
        {
            { '十', 10 }, { '百', 100 }, { '千', 1000 }, { '万', 10000 }, { '亿', 100000000 },
        };

        long total = 0, section = 0, num = 0;
        bool any = false;
        foreach (char ch in s)
        {
            if (digits.TryGetValue(ch, out long d))
            {
                num = d;
                any = true;
            }
            else if (units.TryGetValue(ch, out long u))
            {
                any = true;
                if (u >= 10000)
                {
                    section = (section + num) * u;
                    total += section;
                    section = 0;
                }
                else
                {
                    section += (num == 0 ? 1 : num) * u;
                }

                num = 0;
            }
            else
            {
                return null;
            }
        }

        return any ? total + section + num : null;
    }
}
