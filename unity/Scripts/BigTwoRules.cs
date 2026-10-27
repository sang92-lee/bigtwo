using System.Collections.Generic;
using System.Linq;

// 빅투 규칙: 카드, 덱, 조합 판정, 봇, 점수
public class Card
{
    public int id, r, s; // r: 0=3 … 11=A, 12=2, 13=조커 / s: 0♣ 1♥ 2♦ 3♠ (클 < 하 < 다 < 스)
    public bool Joker => r == 13;
    // 세기: 3 … A < 조커 < ♣2 < ♥2 < ♦2 < ♠2(최강). 조커는 ♣2 바로 아래라 2가 나오면 못 낸다
    public int Val => Joker ? 118 + (s == 3 ? 1 : 0) : r == 12 ? 120 + s * 2 : r * 10 + s * 2;
    public bool Heavy => r >= 12; // 2나 조커: 남으면 점수 ×2

    static readonly string[] Ranks = { "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A", "2" };
    static readonly string[] Suits = { "♣", "♥", "♦", "♠" };
    public string RankText => Joker ? "JOKER" : Ranks[r];
    public string SuitText => Joker ? "★" : Suits[s];
    public bool Red => Joker ? s == 3 : (s == 1 || s == 2);
}

public class Combo
{
    public int type; // 장수: 1, 2, 3, 4(투페어), 5
    public int cat;  // 5장: 1 스트레이트, 2 플러시, 3 풀하우스, 4 포카드, 5 스트레이트 플러시, 6 파이브카드
    public int key;
    public string name;

    public bool Beats(Combo o) => type == o.type && (cat > o.cat || (cat == o.cat && key > o.key));
}

public static class Rules
{
    public static List<Card> MakeDeck(int packs, bool jokers = true)
    {
        var d = new List<Card>();
        int id = 0;
        for (int p = 0; p < packs; p++)
        {
            for (int r = 0; r < 13; r++)
                for (int s = 0; s < 4; s++) d.Add(new Card { id = id++, r = r, s = s });
            if (!jokers) continue;
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

    // 조커는 혼자면 그냥 조커(A < 조커 < 2), 다른 카드와 같이 내면 ♣2까지 아무 카드로 변신 (가장 센 쪽으로)
    public static Combo Eval(IList<Card> cs)
    {
        int nj = cs.Count(x => x.Joker);
        if (cs.Count <= 1 || nj == 0) return EvalPlain(cs);
        var rest = cs.Where(x => !x.Joker).ToList();
        // 변신 후보: 조커가 2장 이하면 3~♣2 전부, 더 많으면 손에 있는 숫자들만 (계산 줄이기)
        var opts = new List<Card>();
        var ranks = nj <= 2 ? Enumerable.Range(0, 13) : rest.Select(x => x.r).Distinct().Append(12);
        foreach (var r in ranks)
            for (int s = 0; s < 4; s++)
                // 같은 숫자와 묶이면 클로버로만, ♣2 위로는 못 감
                if ((r < 12 || s == 0) && (s == 0 || !rest.Any(x => x.r == r))) opts.Add(new Card { id = -2, r = r, s = s });
        Combo best = null;
        void Try(List<Card> cur, int left)
        {
            if (left == 0)
            {
                var cb = EvalPlain(cur);
                if (cb != null && (best == null || cb.Beats(best))) best = cb;
                return;
            }
            foreach (var o in opts) { cur.Add(o); Try(cur, left - 1); cur.RemoveAt(cur.Count - 1); }
        }
        Try(new List<Card>(rest), nj);
        if (best != null) best.name += "(조커)";
        return best;
    }

    static Combo EvalPlain(IList<Card> cs)
    {
        int n = cs.Count;
        if (n == 0) return null;
        var c = cs.OrderBy(x => x.Val).ToList();
        int hi = c[n - 1].Val;
        bool same = c.All(x => x.r == c[0].r);
        if (n == 1) return new Combo { type = 1, key = hi, name = "싱글" };
        if (n == 2 && same) return new Combo { type = 2, key = hi, name = "페어" };
        if (n == 3 && same && !c[0].Joker) return new Combo { type = 3, key = hi, name = "트리플" };
        if (n == 4 && !c.Any(x => x.Joker))
        {
            // 투페어: 서로 다른 숫자 페어 두 개. 높은 페어의 센 카드로 비교
            var g4 = c.GroupBy(x => x.r).ToList();
            if (g4.Count == 2 && g4.All(g => g.Count() == 2)) return new Combo { type = 4, key = hi, name = "투페어" };
            return null;
        }
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
            var jks = hand.Where(x => x.Joker).ToList();
            foreach (var g in byRank.Values)
                foreach (var pick in Subsets(g[0].Joker ? g : g.Concat(jks.Take(2)).ToList(), last.type)) Mark(pick);
            return ok;
        }
        foreach (var m in Moves(hand, last)) foreach (var c in m) ok.Add(c.id); // 조커 섞인 조합
        if (last.type == 4) return ok;

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

    // ---------- 중급 봇: 성격 + 랜덤 ----------
    public class Persona
    {
        public float dump, save, block, passBias, noise;
        public bool hoard;          // 패스로 카드 모아서 큰 조합·용 노리기
        public string name;
        public string[] playLines, passLines;

        // 이름과 성격을 짝지은 봇들 (게임 내내 그 성격)
        public static List<Persona> Cast() => new List<Persona>
        {
            new Persona { name = "칼퇴의 신 이대리", dump = 2.2f, save = 0.2f, block = 1.5f, passBias = 0f, noise = 1f,
                playLines = new[] { "빨리 끝내고 퇴근하자", "다 받아라 나 6시 칼퇴", "가즈아", "아 야근각…" }, passLines = new[] { "부장님 몰래 패스", "...칼퇴 실패" } },
            new Persona { name = "2만 품는 사나이", dump = 0.8f, save = 2.6f, block = 1f, passBias = 0.15f, noise = 0.8f,
                playLines = new[] { "내 2는 결혼할 거야", "2는 절대 안 놔줘", "이 정도면 됐지" }, passLines = new[] { "2 지켰다…", "공짜 카드 개이득" } },
            new Persona { name = "용을 기다리는 할매", hoard = true, dump = 1.4f, save = 1.6f, block = 1f, passBias = 0.2f, noise = 1f,
                playLines = new[] { "옛다 사탕이나 먹어라", "할매 아직 정정하다", "손주들 보고 있냐" }, passLines = new[] { "한 장 더 주게", "용 기다린 지 60년", "모으는 중이여" } },
            new Persona { name = "결재 반려왕 최부장", dump = 1f, save = 1f, block = 2.6f, passBias = 0.05f, noise = 0.9f,
                playLines = new[] { "어딜 나가 회의 중인데", "결재 반려", "못 지나간다" }, passLines = new[] { "이건 다음 분기에", "흠… 보류" } },
            new Persona { name = "광 파는 삼촌", dump = 1f, save = 0.5f, block = 0.8f, passBias = 0.12f, noise = 3.5f,
                playLines = new[] { "이거 고스톱 아니야?", "느낌 왔다", "ㅋㅋ 이거 맞나?", "광 팔게" }, passLines = new[] { "몰라 패스", "삼촌 화장실 좀" } },
        };

        public static Persona Random(System.Random rng) => new Persona
        {
            dump = 0.5f + (float)rng.NextDouble() * 1.5f,      // 여러 장 한꺼번에 털기 좋아함
            save = (float)rng.NextDouble() * 2f,               // 2·조커 아끼기
            block = 0.5f + (float)rng.NextDouble() * 1.5f,     // 상대가 거의 다 냈을 때 막기
            passBias = (float)rng.NextDouble() * 0.35f,        // 일부러 패스하는 버릇
            noise = 0.8f + (float)rng.NextDouble() * 1.6f,     // 엉뚱함
        };
    }

    static bool Strong(Card c) => c.Val >= 110; // 조커·2

    // 낼 수 있는 수 후보 (조합 폭발을 막으려고 대표적인 것만)
    public static List<List<Card>> Moves(List<Card> hand, Combo last)
    {
        var all = new List<List<Card>>();
        var sorted = hand.OrderBy(x => x.Val).ToList();
        var byRank = sorted.GroupBy(x => x.r).ToDictionary(g => g.Key, g => g.ToList());
        bool want(int n) => last == null || last.type == n;
        if (want(1)) all.AddRange(sorted.Select(c => new List<Card> { c }));
        var jk = sorted.Where(x => x.Joker).ToList();
        foreach (var g in byRank.Values)
        {
            var pool = g[0].Joker ? g : g.Concat(jk.Take(2)).ToList(); // 숫자 + 조커
            if (want(2)) all.AddRange(Subsets(pool, 2).Take(16));
            if (want(3)) all.AddRange(Subsets(pool, 3).Take(16));
        }
        if (jk.Count > 0 && (want(4) || want(5)))
        {
            // 조커 1장으로 빈칸 메우기: 다른 카드 4장 + 조커
            var plain = sorted.Where(x => !x.Joker).ToList();
            if (want(4))
                foreach (var p in byRank.Where(kv => kv.Key < 13 && kv.Value.Count >= 2))
                    foreach (var c in plain.Where(x => x.r != p.Key).GroupBy(x => x.r).Select(g => g.First()))
                        all.Add(p.Value.Take(2).Append(c).Append(jk[0]).ToList());
            if (want(5))
            {
                for (int st = 0; st + 4 < 13; st++)
                {
                    var win = Enumerable.Range(st, 5).ToList();
                    var have = win.Where(byRank.ContainsKey).ToList();
                    if (have.Count == 4) all.Add(have.Select(r => byRank[r].First()).Append(jk[0]).ToList());
                }
                foreach (var f in plain.GroupBy(x => x.s).Select(g => g.ToList()).Where(l => l.Count >= 4))
                    all.Add(f.Take(4).Append(jk[0]).ToList());
                foreach (var t in byRank.Where(kv => kv.Key < 13 && kv.Value.Count >= 2))
                    foreach (var p in byRank.Where(kv => kv.Key < 13 && kv.Key != t.Key && kv.Value.Count >= 2))
                        all.Add(t.Value.Take(2).Concat(p.Value.Take(2)).Append(jk[0]).ToList()); // 투페어 + 조커 = 풀하우스
            }
        }
        if (want(4))
        {
            var pairs = byRank.Where(kv => kv.Key < 13 && kv.Value.Count >= 2).Select(kv => kv.Value).ToList();
            for (int i = 0; i < pairs.Count; i++)
                for (int j = i + 1; j < pairs.Count; j++)
                {
                    all.Add(pairs[i].Take(2).Concat(pairs[j].Take(2)).ToList());
                    all.Add(pairs[i].Skip(pairs[i].Count - 2).Concat(pairs[j].Skip(pairs[j].Count - 2)).ToList());
                }
        }
        if (want(5))
        {
            for (int st = 0; st + 4 < 12; st++)
            {
                var win = Enumerable.Range(st, 5).ToList();
                if (!win.All(byRank.ContainsKey)) continue;
                all.Add(win.Select(r => byRank[r].First()).ToList());
                all.Add(win.Select(r => byRank[r].Last()).ToList());
            }
            foreach (var f in sorted.Where(x => !x.Joker).GroupBy(x => x.s).Select(g => g.ToList()).Where(l => l.Count >= 5))
                for (int i = 0; i + 5 <= f.Count; i++) all.Add(f.Skip(i).Take(5).ToList());
            var normal = byRank.Where(kv => kv.Key < 13).ToList();
            foreach (var t in normal.Where(kv => kv.Value.Count >= 3))
                foreach (var p in normal.Where(kv => kv.Key != t.Key && kv.Value.Count >= 2))
                    all.Add(t.Value.Take(3).Concat(p.Value.Take(2)).ToList());
            foreach (var q in normal.Where(kv => kv.Value.Count >= 4))
            {
                var kick = sorted.FirstOrDefault(x => x.r != q.Key && !x.Joker);
                if (kick != null) all.Add(q.Value.Take(4).Append(kick).ToList());
            }
            foreach (var fv in normal.Where(kv => kv.Value.Count >= 5)) all.Add(fv.Value.Take(5).ToList());
        }
        return all.Where(m => { var c = Eval(m); return c != null && (last == null || c.Beats(last)); }).ToList();
    }

    // 후보마다 점수를 매기고, 성격과 랜덤을 섞어 고른다. null이면 패스
    public static List<Card> SmartMove(List<Card> hand, Combo last, int minOpp, Persona me, System.Random rng)
    {
        var moves = Moves(hand, last);
        if (moves.Count == 0) return null;
        bool danger = minOpp <= 2;           // 누가 거의 다 냈다
        bool endgame = hand.Count <= 5;      // 2·조커가 남으면 ×2 벌점
        float Score(List<Card> m)
        {
            var cb = Eval(m);
            float avg = (float)m.Average(c => c.Val) / 120f; // 0~1, 클수록 센 패
            int strong = m.Count(Strong);
            float sc = 0;
            sc += m.Count * 0.6f * me.dump;                        // 많이 털수록 좋음
            sc -= avg * (danger ? -2.5f * me.block : 2.2f);        // 평소엔 약한 패부터, 위험하면 센 패로 막기
            if (!endgame) sc -= strong * 1.2f * me.save;           // 강한 카드는 아끼고
            else sc += strong * 0.8f;                              // 끝판엔 2·조커부터 털기
            if (last == null && cb.type == 5) sc += me.hoard ? 4f : 1.5f; // 5장으로 판 열기 (할매는 특히)
            if (last == null && cb.type > 1) sc += 0.5f * cb.type;  // 판을 열 땐 묶어서 내기 (페어·트리플)
            if (last == null && minOpp <= 3 && cb.type > minOpp) sc += 3f; // 상대가 같은 장수로 못 받게
            if (m.Count == hand.Count) sc += 100;                  // 이걸로 끝나면 무조건
            return sc + (float)rng.NextDouble() * me.noise;        // 같은 상황에도 다르게
        }
        var best = moves.OrderByDescending(Score).First();
        // 일부러 패스: 센 카드를 써야만 받을 수 있고, 급하지 않을 때
        // (패스하면 1장 먹으니 드물게: 강한 카드가 꼭 필요하고 손패가 넉넉할 때만)
        if (last != null && !danger && hand.Count >= 8 && best.All(Strong) && rng.NextDouble() < 0.1 + me.passBias) return null;
        // 할매: 급하지 않으면 패스로 카드를 모아 용(3~A)을 노린다
        if (me.hoard && last != null && !danger && hand.Count < 15 && Eval(best).type != 5 && rng.NextDouble() < 0.45) return null;
        return best;
    }

    // 용: 3부터 A까지 12개 숫자가 다 있으면 판을 여는 차례에 손패를 한 번에 다 내고 1등
    public static bool HasDragon(List<Card> hand)
    {
        var have = new HashSet<int>(hand.Where(c => !c.Joker).Select(c => c.r));
        int missing = Enumerable.Range(0, 12).Count(r => !have.Contains(r));
        return missing <= hand.Count(c => c.Joker); // 빈 숫자는 조커로 메워도 됨
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
        if (last.type == 4) return Moves(hand, last).OrderBy(m => Eval(m).key).FirstOrDefault();
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
