using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace NaturalConsole.NaturalConsoleCode;

/// <summary>
/// Lazily builds an index of every card/relic/potion/event/monster/power/act with its localized
/// (Chinese) display name, generated aliases, and SCREAMING_SNAKE_CASE Id.Entry. Resolves
/// natural-language queries and surfaces ambiguity (multiple entities sharing a name).
/// </summary>
public static class EntityIndex
{
    public enum Kind
    {
        Card,
        Relic,
        Potion,
        Event,
        Monster,
        Power,
        Act,
    }

    public readonly record struct EntityMatch(string Entry, string Title);

    private readonly record struct Entity(Kind Kind, string Entry, string Title, string Pool, string[] Aliases);

    private static List<Entity>? _entities;

    public static void Build()
    {
        if (_entities != null)
        {
            return;
        }

        var list = new List<Entity>();
        try
        {
            // Power class name -> Chinese title, used to generate "<buff>药水" aliases for potions.
            var powerNames = new Dictionary<string, string>();
            foreach (PowerModel p in ModelDb.AllPowers)
            {
                try
                {
                    powerNames[p.GetType().Name] = p.Title.GetFormattedText();
                }
                catch
                {
                    // ignore
                }
            }

            foreach (CardModel c in ModelDb.AllCards)
            {
                list.Add(new Entity(Kind.Card, c.Id.Entry, c.Title, CardPoolOf(c), Array.Empty<string>()));
            }

            foreach (RelicModel r in ModelDb.AllRelics)
            {
                list.Add(new Entity(Kind.Relic, r.Id.Entry, r.Title.GetFormattedText(), "", Array.Empty<string>()));
            }

            foreach (PotionModel p in ModelDb.AllPotions)
            {
                string entry = p.Id.Entry;
                string title = p.Title.GetFormattedText();
                var aliases = new List<string>();
                try
                {
                    string raw = p.DynamicDescription.GetRawText();
                    foreach (Match m in Regex.Matches(raw, @"\{(\w+Power)"))
                    {
                        if (powerNames.TryGetValue(m.Groups[1].Value, out string? cn) && !string.IsNullOrEmpty(cn))
                        {
                            string alias = cn + "药水";
                            if (alias != title && !aliases.Contains(alias))
                            {
                                aliases.Add(alias);
                            }
                        }
                    }
                }
                catch
                {
                    // ignore
                }

                list.Add(new Entity(Kind.Potion, entry, title, "", aliases.ToArray()));
            }

            foreach (EventModel e in ModelDb.AllEvents)
            {
                list.Add(new Entity(Kind.Event, e.Id.Entry, e.Title.GetFormattedText(), "", Array.Empty<string>()));
            }

            foreach (MonsterModel m in ModelDb.Monsters)
            {
                list.Add(new Entity(Kind.Monster, m.Id.Entry, m.Title.GetFormattedText(), "", Array.Empty<string>()));
            }

            foreach (PowerModel p in ModelDb.AllPowers)
            {
                list.Add(new Entity(Kind.Power, p.Id.Entry, p.Title.GetFormattedText(), "", Array.Empty<string>()));
            }

            foreach (ActModel a in ModelDb.Acts)
            {
                list.Add(new Entity(Kind.Act, a.Id.Entry, a.Title.GetFormattedText(), "", Array.Empty<string>()));
            }
        }
        catch (Exception)
        {
            // ModelDb may not be ready yet; leave an empty index rather than crash.
        }

        _entities = list;
    }

    public static void Invalidate() => _entities = null;

    /// <summary>
    /// Returns every distinct entry matching the query (exact title/alias, exact Id, then substring).
    /// For cards, prefers the current character's version. Callers use this to detect ambiguity.
    /// </summary>
    public static List<EntityMatch> ResolveCandidates(Kind kind, string query)
    {
        Build();
        string q = query.Trim();
        if (q.Length == 0 || _entities == null)
        {
            return new List<EntityMatch>();
        }

        List<Entity> candidates = _entities.Where(e => e.Kind == kind).ToList();
        if (candidates.Count == 0)
        {
            return new List<EntityMatch>();
        }

        string? currentPool = kind == Kind.Card ? CurrentCardPoolEntry() : null;

        static bool HasName(Entity e, string q) =>
            e.Title == q || e.Aliases.Any(a => a == q);

        static bool HasSubstring(Entity e, string q)
        {
            if (e.Title.Length > 0 && (e.Title.Contains(q, StringComparison.Ordinal) || q.Contains(e.Title, StringComparison.Ordinal)))
            {
                return true;
            }

            return e.Aliases.Any(a => a.Length > 0 && (a.Contains(q, StringComparison.Ordinal) || q.Contains(a, StringComparison.Ordinal)));
        }

        static bool HasEntry(Entity e, string q)
        {
            string lower = q.ToLowerInvariant();
            return e.Entry.Equals(q, StringComparison.OrdinalIgnoreCase) || e.Entry.ToLowerInvariant().Contains(lower);
        }

        List<Entity> exact = candidates.Where(e => HasName(e, q)).ToList();
        List<Entity> byEntry = exact.Count == 0 ? candidates.Where(e => HasEntry(e, q)).ToList() : new List<Entity>();
        List<Entity> matched = exact.Count > 0 ? exact : (byEntry.Count > 0 ? byEntry : candidates.Where(e => HasSubstring(e, q)).ToList());

        List<Entity> distinct = matched.GroupBy(e => e.Entry).Select(g => g.First()).ToList();

        // For cards, collapse to the current character's version when available.
        if (kind == Kind.Card && currentPool != null)
        {
            List<Entity> preferred = distinct.Where(e => e.Pool == currentPool).ToList();
            if (preferred.Count > 0)
            {
                distinct = preferred;
            }
        }

        distinct = distinct
            .OrderBy(e => e.Title == q ? 0 : 1)
            .ThenByDescending(e => e.Title.Length)
            .ToList();

        return distinct.Select(e => new EntityMatch(e.Entry, e.Title)).ToList();
    }

    /// <summary>
    /// Single-result resolution (first candidate). Used where ambiguity is not expected.
    /// </summary>
    public static string? Resolve(Kind kind, string query)
    {
        List<EntityMatch> matches = ResolveCandidates(kind, query);
        return matches.Count > 0 ? matches[0].Entry : null;
    }

    /// <summary>
    /// Finds entity names (cards/relics/potions/events/powers/monsters) that extend the longest
    /// trailing suffix of <paramref name="input"/>, for Tab auto-completion.
    /// </summary>
    public static List<string> FindCompletions(string input, out int suffixLength)
    {
        suffixLength = 0;
        var result = new List<string>();
        Build();
        if (_entities == null || string.IsNullOrEmpty(input))
        {
            return result;
        }

        for (int len = input.Length; len >= 1; len--)
        {
            string frag = input.Substring(input.Length - len);
            var titles = _entities
                .Where(e => e.Kind != Kind.Act && (e.Title.StartsWith(frag, StringComparison.Ordinal) || e.Aliases.Any(a => a.StartsWith(frag, StringComparison.Ordinal))))
                .Select(e => e.Title)
                .Distinct()
                .ToList();
            if (titles.Count > 0)
            {
                suffixLength = len;
                result = titles;
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the closest entity names for a query, used for "not found" suggestions.
    /// </summary>
    public static List<string> Suggest(Kind kind, string query, int count = 5)
    {
        Build();
        string q = query.Trim();
        if (_entities == null || q.Length == 0)
        {
            return new List<string>();
        }

        return _entities
            .Where(e => e.Kind == kind && e.Title.Length > 0)
            .OrderByDescending(e => Overlap(e.Title, q))
            .ThenBy(e => e.Title.Length)
            .ThenBy(e => e.Title, StringComparer.Ordinal)
            .Take(count)
            .Select(e => e.Title)
            .Distinct()
            .ToList();
    }

    private static int Overlap(string title, string query)
    {
        int score = 0;
        for (int i = 0; i < title.Length - 1; i++)
        {
            if (query.Contains(title.Substring(i, 2), StringComparison.Ordinal))
            {
                score++;
            }
        }

        return score;
    }

    private static string CardPoolOf(CardModel card)
    {
        try
        {
            return card.Pool?.Id.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string? CurrentCardPoolEntry()
    {
        try
        {
            var me = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
            return me?.Character?.CardPool?.Id.ToString();
        }
        catch
        {
            return null;
        }
    }
}
