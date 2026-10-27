using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

// 온라인 방: 방장이 판정을 맡고(host), 참가자에게는 자기 손패만 보낸다.
// 연결은 Plugins/WebGL/BigTwoNet.jslib (PeerJS). 에디터에서는 혼자 연습만 된다.
public partial class BigTwoGame
{
    enum NetMode { Solo, Host, Client }
    NetMode mode = NetMode.Solo;
    string roomCode = "", myPeer = "", netStatus = "", inviteCode = "";
    int lobbySeats = 4;
    bool micOn;
    readonly Dictionary<string, float> speak = new Dictionary<string, float>();

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void NetInit();
    [DllImport("__Internal")] static extern void NetHost(string code);
    [DllImport("__Internal")] static extern void NetJoin(string code);
    [DllImport("__Internal")] static extern void NetSend(string to, string msg);
    [DllImport("__Internal")] static extern void NetSetPeers(string csv);
    [DllImport("__Internal")] static extern void NetMic(bool on);
    [DllImport("__Internal")] static extern void NetLeave();
    [DllImport("__Internal")] static extern string NetRoomParam();
    [DllImport("__Internal")] static extern string NetLink(string code);
    [DllImport("__Internal")] static extern void NetCopy(string text);
    [DllImport("__Internal")] static extern void NetAsk(string key, string title, string def);
    static bool Online => true;
#else
    static void NetInit() { }
    static void NetHost(string code) { }
    static void NetJoin(string code) { }
    static void NetSend(string to, string msg) { }
    static void NetSetPeers(string csv) { }
    static void NetMic(bool on) { }
    static void NetLeave() { }
    static string NetRoomParam() => "";
    static string NetLink(string code) => "?room=" + code;
    static void NetCopy(string text) { GUIUtility.systemCopyBuffer = text; }
    static void NetAsk(string key, string title, string def) { }
    static bool Online => false;
#endif

    [Serializable] class NetEvt { public string t, from, id, msg; public string[] ids; public float[] vs; public bool on; }
    [Serializable] class Msg { public string k, name; public int[] ids; }
    [Serializable] class CardDto { public int id, r, s; }
    [Serializable] class ResDto { public int who, left, heavy, score; }
    [Serializable]
    class View
    {
        public string k = "state";
        public int phase, you, turn, lastBy, round, packs, pile, lobbySeats;
        public float timeLeft;
        public bool hasLast;
        public string lastText, room;
        public string[] names, peers;
        public bool[] bots;
        public int[] counts, totals;
        public CardDto[] hand, last;
        public ResDto[] results;
    }

    void InitNet()
    {
        if (!Online) return;
        NetInit();
        inviteCode = (NetRoomParam() ?? "").Trim().ToUpper();
    }

    string pendingJoin = "";

    // 이름이 없으면 입력창을 띄우고 false (입력이 오면 OnNet "ask"에서 이어서 진행)
    bool HaveName(string then)
    {
        if (!string.IsNullOrWhiteSpace(myName)) return true;
        NetAsk(then, "내 이름 (8자까지)", "");
        return false;
    }

    void SetName(string v)
    {
        v = (v ?? "").Trim();
        if (v.Length > 8) v = v.Substring(0, 8);
        myName = v;
        PlayerPrefs.SetString("name", myName);
    }

    // ---------- 방 만들기 / 참가 ----------

    void CreateRoom()
    {
        if (!Online) { Toast("온라인 방은 웹에서만 돼요"); return; }
        if (!HaveName("name-create")) return;
        mode = NetMode.Host;
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        roomCode = new string(Enumerable.Range(0, 4).Select(_ => letters[rng.Next(letters.Length)]).ToArray());
        players.Clear();
        players.Add(new Player { name = myName, peer = "bigtwo-" + roomCode });
        lobbySeats = 4;
        phase = Phase.Lobby;
        netStatus = "방 여는 중…";
        LobbyRefresh();
        NetHost(roomCode);
    }

    void JoinRoom(string code)
    {
        if (!Online) { Toast("온라인 방은 웹에서만 돼요"); return; }
        code = (code ?? "").Trim().ToUpper();
        if (code.Length != 4) { NetAsk("code", "방 코드 4글자", ""); return; }
        pendingJoin = code;
        if (!HaveName("name-join")) return;
        mode = NetMode.Client;
        roomCode = code;
        players.Clear();
        phase = Phase.Lobby;
        netStatus = "방에 들어가는 중…";
        NetJoin(code);
    }

    void LeaveRoom(string why = null)
    {
        if (mode != NetMode.Solo) NetLeave();
        mode = NetMode.Solo;
        micOn = false;
        speak.Clear();
        phase = Phase.Menu;
        if (why != null) Toast(why);
    }

    void StartOnline()
    {
        numPlayers = Mathf.Max(lobbySeats, players.Count);
        string[] botNames = { "검은 모자", "말 없는 자", "그림자" };
        for (int i = players.Count, b = 0; i < numPlayers; i++, b++)
            players.Add(new Player { name = botNames[b % 3], bot = true });
        round = 0;
        PlaceSeats();
        StartRound();
    }

    // 대기실: 들어온 사람만 원탁에 앉힌다
    void LobbyRefresh()
    {
        numPlayers = Mathf.Max(1, players.Count);
        PlaceSeats();
        RefreshSeats();
        for (int k = 1; k < players.Count; k++) players[k].seat.label.text = players[k].name;
    }

    // ---------- 브라우저에서 오는 소식 ----------

    public void OnNet(string json)
    {
        var e = JsonUtility.FromJson<NetEvt>(json);
        switch (e.t)
        {
            case "ready":
                myPeer = e.id;
                netStatus = "";
                if (mode == NetMode.Client) NetSend("bigtwo-" + roomCode, JsonUtility.ToJson(new Msg { k = "join", name = myName }));
                else Sync();
                break;
            case "error":
                if (mode == NetMode.Host && e.msg == "unavailable-id") { NetLeave(); CreateRoom(); break; } // 코드 겹침 → 새 코드
                LeaveRoom(e.msg == "peer-unavailable" ? "그 방을 찾을 수 없어요" : e.msg == "load" ? "연결 준비에 실패했어요" : "연결이 끊겼어요");
                break;
            case "data":
                if (mode == NetMode.Host) OnClientMsg(e.from, e.msg);
                else OnHostMsg(e.msg);
                break;
            case "leave":
                if (mode == NetMode.Client) { LeaveRoom("방장이 나갔어요"); break; }
                OnClientLeft(e.from);
                break;
            case "ask":
                if (e.id == "code") { if (!string.IsNullOrWhiteSpace(e.msg)) JoinRoom(e.msg); break; }
                if (string.IsNullOrWhiteSpace(e.msg)) break;
                SetName(e.msg);
                if (e.id == "name-create") CreateRoom();
                else if (e.id == "name-join") JoinRoom(pendingJoin);
                break;
            case "mic":
                micOn = e.on;
                if (e.msg == "denied") Toast("마이크 권한을 허용해 주세요");
                break;
            case "lv":
                for (int i = 0; i < e.ids.Length; i++) speak[e.ids[i]] = e.vs[i];
                break;
        }
    }

    void OnClientMsg(string from, string json)
    {
        var m = JsonUtility.FromJson<Msg>(json);
        int i = players.FindIndex(p => p.peer == from);
        if (m.k == "join")
        {
            if (phase != Phase.Lobby || players.Count >= 4) { NetSend(from, JsonUtility.ToJson(new Msg { k = "full" })); return; }
            if (i < 0) players.Add(new Player { name = string.IsNullOrWhiteSpace(m.name) ? "손님" : m.name, peer = from });
            lobbySeats = Mathf.Max(lobbySeats, players.Count);
            sfx.PlayOneShot(turnClip, 0.5f);
            LobbyRefresh();
            Sync();
            return;
        }
        if (i < 0 || phase != Phase.Play || turn != i) return;
        if (m.k == "play")
        {
            var cards = (m.ids ?? new int[0]).Select(id => players[i].hand.FirstOrDefault(c => c.id == id)).ToList();
            if (cards.Count == 0 || cards.Any(c => c == null)) return;
            Play(i, cards);
        }
        else if (m.k == "pass" && lastCombo != null) Pass(i);
    }

    void OnClientLeft(string from)
    {
        int i = players.FindIndex(p => p.peer == from);
        if (i <= 0) return;
        if (phase == Phase.Lobby) { players.RemoveAt(i); LobbyRefresh(); }
        else
        {
            var p = players[i];
            Toast($"{p.name} 나감 · 봇이 대신해요");
            p.bot = true; p.peer = null; p.name += "(봇)";
            if (phase == Phase.Play && turn == i) botTimer = 1f;
            RefreshSeats();
        }
        Sync();
    }

    // ---------- 방장 → 참가자 ----------

    void Sync()
    {
        if (mode != NetMode.Host) return;
        NetSetPeers(string.Join(",", players.Where(p => p.peer != null && p.peer != myPeer).Select(p => p.peer)));
        for (int i = 1; i < players.Count; i++)
            if (!players[i].bot && players[i].peer != null) NetSend(players[i].peer, JsonUtility.ToJson(ViewFor(i)));
    }

    static CardDto[] Dto(IEnumerable<Card> cs) => cs.Select(c => new CardDto { id = c.id, r = c.r, s = c.s }).ToArray();

    View ViewFor(int i) => new View
    {
        phase = (int)phase, you = i, turn = turn, lastBy = lastBy, round = round, packs = packs, pile = drawPile.Count,
        lobbySeats = lobbySeats, timeLeft = Mathf.Max(0, turnEnd - Time.time), hasLast = lastCombo != null,
        lastText = lastText, room = roomCode,
        names = players.Select(p => p.name).ToArray(),
        peers = players.Select(p => p.peer ?? "").ToArray(),
        bots = players.Select(p => p.bot).ToArray(),
        counts = players.Select(p => p.hand.Count).ToArray(),
        totals = players.Select(p => p.total).ToArray(),
        hand = Dto(players[i].hand), last = Dto(lastCards),
        results = results == null ? new ResDto[0] : results.Select(r => new ResDto { who = players.IndexOf(r.p), left = r.left, heavy = r.heavy, score = r.score }).ToArray(),
    };

    // ---------- 참가자: 방장이 보낸 판을 내 자리 기준으로 돌려서 그린다 ----------

    void OnHostMsg(string json)
    {
        var m = JsonUtility.FromJson<Msg>(json);
        if (m.k == "full") { LeaveRoom("방이 꽉 찼거나 이미 시작했어요"); return; }
        if (m.k != "state") return;
        var v = JsonUtility.FromJson<View>(json);
        int n = v.names.Length, prevTurn = turn, prevLast = lastCards.Count > 0 ? lastCards[0].id : -1;
        var prevPhase = phase;
        int R(int g) => g < 0 ? -1 : ((g - v.you) % n + n) % n;

        var prevMine = players.Count > 0 ? new HashSet<int>(players[0].hand.Select(c => c.id)) : null;
        var prevCounts = players.GroupBy(p => p.name).ToDictionary(g => g.Key, g => g.First().hand.Count);
        players.Clear();
        for (int l = 0; l < n; l++)
        {
            int g = (l + v.you) % n;
            var p = new Player { name = v.names[g], bot = v.bots[g], total = v.totals[g], peer = v.peers[g] };
            p.hand = l == 0 ? v.hand.Select(c => new Card { id = c.id, r = c.r, s = c.s }).ToList()
                            : Enumerable.Range(0, v.counts[g]).Select(_ => new Card { id = -1 }).ToList();
            players.Add(p);
        }
        phase = (Phase)v.phase;
        packs = v.packs; round = v.round; lobbySeats = v.lobbySeats;
        numPlayers = n;
        PlaceSeats();
        turn = R(v.turn); lastBy = R(v.lastBy);
        lastCards = v.last.Select(c => new Card { id = c.id, r = c.r, s = c.s }).ToList();
        lastCombo = v.hasLast ? Rules.Eval(lastCards) : null;
        lastText = v.lastText;
        drawPile = Enumerable.Range(0, v.pile).Select(_ => new Card { id = -1 }).ToList();
        turnEnd = Time.time + v.timeLeft;
        results = phase == Phase.End
            ? v.results.Select(r => new Result { p = players[R(r.who)], left = r.left, heavy = r.heavy, score = r.score }).ToList()
            : null;

        if (phase == Phase.Play)
        {
            int newLast = lastCards.Count > 0 ? lastCards[0].id : -1;
            if (newLast != prevLast && newLast >= 0) OnCardsPlayed(lastBy, lastCards.Count);
            if (turn == 0 && (prevTurn != 0 || prevPhase != Phase.Play))
            {
                sfx.PlayOneShot(turnClip, 0.9f);
                selected.Clear();
                lastTick = 99;
            }
            playable = Rules.Playable(players[0].hand, lastCombo);
        }
        if (phase == Phase.Play && prevPhase == Phase.Play)
        {
            // 패스해서 먹은 카드: 나는 날아오는 앞면, 남은 뒷면이 그 자리로
            var got = prevMine == null ? null : players[0].hand.FirstOrDefault(c => !prevMine.Contains(c.id));
            if (got != null) ShowDraw(0, got);
            for (int k = 1; k < players.Count; k++)
                if (prevCounts.TryGetValue(players[k].name, out var pc) && players[k].hand.Count == pc + 1) ShowDraw(k, null);
        }
        for (int k = 1; k < players.Count; k++) players[k].seat.halo.intensity = phase == Phase.Play && turn == k ? 2.2f : 0;
        RefreshSeats();
        NetSetPeers(string.Join(",", players.Skip(1).Where(p => !string.IsNullOrEmpty(p.peer)).Select(p => p.peer)));
    }

    void SendAction(string k, List<Card> cards = null) =>
        NetSend("bigtwo-" + roomCode, JsonUtility.ToJson(new Msg { k = k, ids = cards?.Select(c => c.id).ToArray() }));

    // ---------- 대기실 화면 ----------

    void DrawLobby(float W)
    {
        var dim = new Color(0.72f, 0.68f, 0.62f);
        float cx = W / 2;
        GUI.Label(new Rect(0, 130, W, 40), "방 코드", Style(26, dim));
        GUI.Label(new Rect(0, 165, W, 90), roomCode, Style(80, new Color(0.95f, 0.55f, 0.4f), TextAnchor.MiddleCenter, FontStyle.Bold));
        if (Btn(new Rect(cx - 130, 262, 260, 60), "초대 링크 복사")) { NetCopy(NetLink(roomCode)); Toast("링크를 복사했어요"); }

        GUI.Label(new Rect(0, 340, W, 36), netStatus != "" ? netStatus : $"들어온 사람 {players.Count}/4", Style(26, dim));
        for (int i = 0; i < 4; i++)
        {
            string line = i < players.Count
                ? players[i].name + (i == 0 && mode == NetMode.Host ? "  (나·방장)" : i == 0 ? "  (나)" : players[i].peer == "bigtwo-" + roomCode ? "  (방장)" : "")
                : "대기 중…";
            GUI.Label(new Rect(0, 382 + i * 40, W, 38), line, Style(28, i < players.Count ? Color.white : new Color(0.45f, 0.43f, 0.4f)));
        }

        if (mode == NetMode.Host)
        {
            float bw = 120, bh = 60;
            GUI.Label(new Rect(cx - 380, 560, 160, bh), "자리", Style(24, dim, TextAnchor.MiddleRight));
            for (int k = 2; k <= 4; k++)
                if (Btn(new Rect(cx - 210 + (k - 2) * (bw + 10), 560, bw, bh), $"{k}명", k >= players.Count, lobbySeats == k)) { lobbySeats = k; Sync(); }
            GUI.Label(new Rect(cx - 380, 630, 160, bh), "카드", Style(24, dim, TextAnchor.MiddleRight));
            for (int k = 1; k <= 2; k++)
                if (Btn(new Rect(cx - 210 + (k - 1) * (bw + 10), 630, bw, bh), $"{k}팩", true, packs == k)) { packs = k; Sync(); }
            int bots = Mathf.Max(0, lobbySeats - players.Count);
            GUI.Label(new Rect(0, 700, W, 30), bots > 0 ? $"빈자리 {bots}개는 봇이 앉아요" : "자리가 다 찼어요", Style(22, new Color(0.55f, 0.52f, 0.5f)));
            if (Btn(new Rect(cx - 150, 740, 300, 84), "시작", true, true)) StartOnline();
        }
        else
            GUI.Label(new Rect(0, 600, W, 80), $"방장이 시작하기를 기다리는 중…\n{lobbySeats}명 · 카드 {packs}팩", Style(26, dim));

        if (Btn(new Rect(cx - 150, 860, 140, 60), "나가기")) LeaveRoom();
        if (Btn(new Rect(cx + 10, 860, 140, 60), micOn ? "마이크 끄기" : "마이크 켜기", true, micOn)) NetMic(!micOn);
    }
}
