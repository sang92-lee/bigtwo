using System.Collections.Generic;
using System.Linq;

// 빅투 규칙: 카드, 덱, 조합 판정, 봇, 점수
public class Card
{
    public int id, r, s; // r: 0=3 … 11=A, 12=2, 13=조커 / s: 0♦ 1♣ 2♥ 3♠
    public bool Joker => r == 13;
    public int Val => r == 12 && s == 3 ? 62 : r * 4 + s; // ♠2는 조커보다도 높은 최강 카드
    public bool Heavy => r >= 12; // 2나 조커: 남으면 점수 ×2

    static readonly string[] Ranks = { "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A", "2" };
    static readonly string[] Suits = { "♦", "♣", "♥", "♠" };
    public string RankText => Joker ? "JOKER" : Ranks[r];
    public string SuitText => Joker ? "★" : Suits[s];
    public bool Red => Joker ? s == 3 : (s == 0 || s == 2);
}

public class Combo
{
    public int type; // 장수: 1, 2, 3, 5
    public int cat;  // 5장: 1 스트레이트, 2 플러시, 3 풀하우스, 4 포카드, 5 스트레이트 플러시, 6 파이브카드
    public int key;
    public string name;

    public bool Beats(Combo o) => type == o.type && (cat > o.cat || (cat == o.cat && key > o.key));
}

public static class Rules
{
    public static List<Card> MakeDeck(int packs)
    {
        var d = new List<Card>();
        int id = 0;
        for (int p = 0; p < packs; p++)
        {
            for (int r = 0; r < 13; r++)
                for (int s = 0; s < 4; s++) d.Add(new Card { id = id++, r = r, s = s });
            d.Add(new Card { id = id++, r = 13, s = 0 }); // 흑백 조커
            d.Add(new Card { id = id++, r = 13, s = 3 }); // 컬러 조커
        }
        return d;
    }

    public static void Shuffle<T>(IList<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public static Combo Eval(IList<Card> cs)
    {
        int n = cs.Count;
        if (n == 0) return null;
        var c = cs.OrderBy(x => x.Val).ToList();
        int hi = c[n - 1].Val;
        bool same = c.All(x => x.r == c[0].r);
        if (n == 1) return new Combo { type = 1, key = hi, name = "싱글" };
        if (n == 2 && same) return new Combo { type = 2, key = hi, name = "페어" };
        if (n == 3 && same && !c[0].Joker) return new Combo { type = 3, key = hi, name = "트리플" };
        if (n != 5 || c.Any(x => x.Joker)) return null;

        if (same) return new Combo { type = 5, cat = 6, key = hi, name = "파이브카드" };
        var rs = c.Select(x => x.r).ToList();
        bool flush = c.All(x => x.s == c[0].s);
        bool straight = rs[4] < 12; // 2는 스트레이트에 못 들어감
        for (int i = 1; i < 5; i++) if (rs[i] != rs[i - 1] + 1) straight = false;
        var groups = rs.GroupBy(r => r).OrderByDescending(g => g.Count()).ToList();

        if (straight && flush) return new Combo { type = 5, cat = 5, key = hi, name = "스트레이트 플러시" };
        if (groups[0].Count() == 4) return new Combo { type = 5, cat = 4, key = groups[0].Key, name = "포카드" };
        if (groups[0].Count() == 3 && groups[1].Count() == 2) return new Combo { type = 5, cat = 3, key = groups[0].Key, name = "풀하우스" };
        if (flush) return new Combo { type = 5, cat = 2, key = hi, name = "플러시" };
        if (straight) return new Combo { type = 5, cat = 1, key = hi, name = "스트레이트" };
        return null;
    }

    // 남은 카드 수 × (2나 조커 한 장마다 2배)
    public static int Score(List<Card> hand)
    {
        int heavy = hand.Count(c => c.Heavy);
        return hand.Count * (1 << heavy);
    }

    // 지금 낼 수 있는 조합에 들어갈 수 있는 카드 id들 (판을 여는 차례면 전부)
    public static HashSet<int> Playable(List<Card> hand, Combo last)
    {
        var ok = new HashSet<int>();
        if (last == null) { foreach (var c in hand) ok.Add(c.id); return ok; }
        void Mark(List<Card> pick)
        {
            var cb = Eval(pick);
            if (cb != null && cb.Beats(last)) foreach (var c in pick) ok.Add(c.id);
        }
        var sorted = hand.OrderBy(x => x.Val).ToList();
        var byRank = sorted.GroupBy(x => x.r).ToDictionary(g => g.Key, g => g.ToList());

        if (last.type <= 3)
        {
            foreach (var g in byRank.Values)
                foreach (var pick in Subsets(g, last.type)) Mark(pick);
            return ok;
        }

        // 5장: 각 카드마다 "이 카드 + 나머지는 가장 센 카드"로 이길 수 있는지 본다
        Card Top(int r, Card not = null) => byRank[r].Where(x => x != not).LastOrDefault();
        for (int start = 0; start + 4 < 12; start++)
        {
            var win = Enumerable.Range(start, 5).ToList();
            if (!win.All(byRank.ContainsKey)) continue;
            foreach (var r in win)
                foreach (var c in byRank[r])
                    Mark(win.Select(x => x == r ? c : Top(x)).ToList());
        }
        foreach (var f in sorted.Where(x => !x.Joker).GroupBy(x => x.s).Where(g => g.Count() >= 5))
        {
            var suit = f.ToList();
            foreach (var c in suit) Mark(suit.Where(x => x != c).Reverse().Take(4).Append(c).ToList());
        }
        var normal = byRank.Where(kv => kv.Key < 13).ToList();
        foreach (var t in normal.Where(kv => kv.Value.Count >= 3))
            foreach (var p in normal.Where(kv => kv.Key != t.Key && kv.Value.Count >= 2))
            {
                var pick = t.Value.Skip(t.Value.Count - 3).Concat(p.Value.Take(2)).ToList();
                var cb = Eval(pick);
                if (cb != null && cb.Beats(last)) { foreach (var c in t.Value) ok.Add(c.id); foreach (var c in p.Value) ok.Add(c.id); }
            }
        foreach (var q in normal.Where(kv => kv.Value.Count >= 4))
        {
            var kick = sorted.FirstOrDefault(x => x.r != q.Key && !x.Joker);
            if (kick == null) continue;
            var cb = Eval(q.Value.Take(4).Append(kick).ToList());
            if (cb != null && cb.Beats(last)) foreach (var c in sorted.Where(x => !x.Joker)) ok.Add(c.id); // 포카드 + 아무 카드
        }
        foreach (var five in normal.Where(kv => kv.Value.Count >= 5))
            foreach (var pick in Subsets(five.Value, 5)) Mark(pick);
        return ok;
    }

    static IEnumerable<List<Card>> Subsets(List<Card> src, int k, int start = 0)
    {
        if (k == 0) { yield return new List<Card>(); yield break; }
        for (int i = start; i <= src.Count - k; i++)
            foreach (var rest in Subsets(src, k - 1, i + 1))
            {
                rest.Insert(0, src[i]);
                yield return rest;
            }
    }

    // 봇: 낼 수 있는 것 중 가장 약한 패를 낸다. 못 내면 null(패스)
    public static List<Card> BotMove(List<Card> hand, Combo last)
    {
        var sorted = hand.OrderBy(x => x.Val).ToList();
        var byRank = sorted.GroupBy(x => x.r).ToList();
        if (last == null)
        {
            var low = sorted[0];
            var same = sorted.Where(x => x.r == low.r).ToList();
            if (same.Count >= 3 && !low.Joker) return same.Take(3).ToList();
            if (same.Count >= 2) return same.Take(2).ToList();
            return new List<Card> { low };
        }
        if (last.type <= 3)
        {
            foreach (var g in byRank)
            {
                var list = g.ToList();
                for (int i = 0; i + last.type <= list.Count; i++)
                {
                    var pick = list.Skip(i).Take(last.type).ToList();
                    var cb = Eval(pick);
                    if (cb != null && cb.Beats(last)) return pick;
                }
            }
            return null;
        }
        // 5장: 스트레이트, 풀하우스, 포카드 순으로 찾아본다
        List<Card> best = null; Combo bestC = null;
        void Try(List<Card> pick)
        {
            var cb = Eval(pick);
            if (cb == null || !cb.Beats(last)) return;
            if (bestC == null || bestC.Beats(cb)) { best = pick; bestC = cb; }
        }
        var ranks = byRank.ToDictionary(g => g.Key, g => g.ToList());
        for (int start = 0; start + 4 < 12; start++)
        {
            if (!Enumerable.Range(start, 5).All(ranks.ContainsKey)) continue;
            Try(Enumerable.Range(start, 5).Select(r => ranks[r][0]).ToList());
        }
        foreach (var t in byRank.Where(g => g.Count() >= 3 && g.Key < 13))
            foreach (var p in byRank.Where(g => g.Key != t.Key && g.Count() >= 2 && g.Key < 13))
            { Try(t.Take(3).Concat(p.Take(2)).ToList()); break; }
        foreach (var q in byRank.Where(g => g.Count() >= 4 && g.Key < 13))
        {
            var kick = sorted.FirstOrDefault(x => x.r != q.Key && !x.Joker);
            if (kick != null) Try(q.Take(4).Append(kick).ToList());
        }
        foreach (var f in sorted.Where(x => !x.Joker).GroupBy(x => x.s).Where(g => g.Count() >= 5))
            Try(f.Take(5).ToList());
        return best;
    }
}
