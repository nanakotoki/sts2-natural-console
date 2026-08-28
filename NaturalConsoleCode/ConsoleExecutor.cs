using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace NaturalConsole.NaturalConsoleCode;

/// <summary>
/// Owns a private <see cref="DevConsole"/> configured to allow debug commands, so the mod can execute
/// any built-in console command without the player having to enable the full console.
/// </summary>
public static class ConsoleExecutor
{
    // Special markers produced by the parser (not real console commands).
    public const string UpgradedCardPrefix = "!card+ ";      // "!card+ <entry>" - one upgraded card
    public const string AddCardPrefix = "!cardN ";           // "!cardN <count> <entry> [pile]" - N base cards
    public const string AddUpgradedCardPrefix = "!cardN+ ";  // "!cardN+ <count> <entry> [pile]" - N upgraded cards

    private static DevConsole? _console;

    public static DevConsole Console => _console ??= new DevConsole(shouldAllowDebugCommands: true);

    public static void Init()
    {
        // The DevConsole is created lazily on first use. Creating it here (during
        // ModManager.Initialize) would fail because ReflectionHelper can't scan mod types yet.
    }

    /// <summary>
    /// Executes a parsed command string and returns a user-facing result message.
    /// </summary>
    public static string Execute(string command)
    {
        if (command.StartsWith(AddUpgradedCardPrefix, StringComparison.Ordinal))
        {
            string[] parts = command.Substring(AddUpgradedCardPrefix.Length).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out int count) && count > 0)
            {
                return AddCards(parts[1], count, parts.Length >= 3 ? parts[2] : null, upgraded: true);
            }

            return "失败：无效的卡牌数量";
        }

        if (command.StartsWith(UpgradedCardPrefix, StringComparison.Ordinal))
        {
            return AddCards(command.Substring(UpgradedCardPrefix.Length).Trim(), 1, null, upgraded: true);
        }

        if (command.StartsWith(AddCardPrefix, StringComparison.Ordinal))
        {
            string[] parts = command.Substring(AddCardPrefix.Length).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out int count) && count > 0)
            {
                return AddCards(parts[1], count, parts.Length >= 3 ? parts[2] : null, upgraded: false);
            }

            return "失败：无效的卡牌数量";
        }

        try
        {
            CmdResult result = Console.ProcessCommand(command);
            return result.success
                ? (string.IsNullOrEmpty(result.msg) ? "完成" : result.msg)
                : "失败：" + result.msg;
        }
        catch (Exception e)
        {
            return "执行出错：" + e.Message;
        }
    }

    /// <summary>
    /// Adds <paramref name="count"/> copies of a card (optionally upgraded) to a pile, refreshing the
    /// pile count UI afterwards.
    /// </summary>
    private static string AddCards(string entry, int count, string? pile, bool upgraded)
    {
        try
        {
            PileType pileType;
            bool inCombat = CombatManager.Instance.IsInProgress;
            if (!string.IsNullOrEmpty(pile))
            {
                if (!Enum.TryParse<PileType>(pile, true, out pileType) || pileType == PileType.None)
                {
                    return "失败：无效的牌堆 " + pile;
                }
            }
            else
            {
                pileType = inCombat ? PileType.Hand : PileType.Deck;
            }

            var me = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
            if (me == null)
            {
                return "失败：当前没有进行中的对局";
            }

            CardModel? canonical = ModelDb.AllCards.FirstOrDefault(c => c.Id.Entry == entry);
            if (canonical == null)
            {
                return "失败：找不到卡牌 " + entry;
            }

            var runState = RunManager.Instance.DebugOnlyGetState();
            CombatState? combatState = pileType.IsCombatPile() ? CombatManager.Instance.DebugOnlyGetState() : null;
            if (pileType.IsCombatPile() && combatState == null)
            {
                return "失败：当前不在战斗中（" + PileLabel(pileType) + " 需要在战斗中操作）";
            }

            if (!pileType.IsCombatPile() && runState == null)
            {
                return "失败：当前没有进行中的对局";
            }

            var cards = new List<CardModel>(count);
            for (int i = 0; i < count; i++)
            {
                CardModel card = pileType.IsCombatPile()
                    ? combatState!.CreateCard(canonical, me)
                    : runState!.CreateCard(canonical, me);
                if (upgraded && card.IsUpgradable)
                {
                    card.UpgradeInternal();
                    card.FinalizeUpgradeInternal();
                }

                cards.Add(card);
            }

            Task task = CardPileCmd.Add(cards, pileType);
            TaskHelper.RunSafely(task);

            // The pile count labels increment once per CardAddFinished event, so fire it once per
            // card so the count increases by the actual number added.
            try
            {
                CardPile pileRef = pileType.GetPile(me);
                for (int i = 0; i < count; i++)
                {
                    pileRef.InvokeCardAddFinished();
                }
            }
            catch
            {
                // ignore
            }

            string up = upgraded ? "升级版 " : "";
            return $"已添加 {count} 张{up}{canonical.Title}" + (upgraded ? " (+1)" : "") + $" 到{PileLabel(pileType)}";
        }
        catch (Exception e)
        {
            MainFile.Logger.Error("[AddCards] " + e);
            return "执行出错：" + e.Message;
        }
    }

    private static string PileLabel(PileType pile) => pile switch
    {
        PileType.Hand => "手牌",
        PileType.Deck => "牌组",
        PileType.Draw => "抽牌堆",
        PileType.Discard => "弃牌堆",
        PileType.Exhaust => "消耗牌堆",
        PileType.Play => "打出堆",
        _ => pile.ToString(),
    };
}
