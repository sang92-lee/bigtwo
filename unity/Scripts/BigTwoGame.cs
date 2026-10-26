using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 빅투 한 판: 어두운 방, 원탁, 1인칭 시점. 나머지 자리는 봇.
// 장면은 전부 코드로 만든다 (Main 씬은 비어 있어도 됨)
public partial class BigTwoGame : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<BigTwoGame>() == null) new GameObject("BigTwo").AddComponent<BigTwoGame>();
    }

    class Player
    {
        public string name;
        public bool bot;
        public List<Card> hand = new List<Card>();
        public int total;
        public Seat seat;
        public string peer; // 온라인: 브라우저 연결 id (봇·혼자 모드는 null)
    }

    class Seat
    {
        public GameObject root;
        public TextMesh label;
        public Light halo;
        public Transform pile;
    }

    class Result { public Player p; public int left, heavy, score; }

    enum Phase { Menu, Lobby, Play, End }

    Phase phase = Phase.Menu;
    int numPlayers = 4, packs = 1;
    string myName;
    readonly List<Player> players = new List<Player>();
    readonly Seat[] seats = new Seat[4];
    int turn, lastBy = -1, passes, round;
    Combo lastCombo;
    List<Card> lastCards = new List<Card>();
    List<Card> drawPile = new List<Card>(); // 처음 9장씩 나누고 남은 카드. 패스하면 1장 먹기
    const int DealCount = 9;
    Transform pileStack;
    string lastText = "", toast = "";
    float toastUntil, botTimer;
    readonly HashSet<int> selected = new HashSet<int>();
    HashSet<int> playable = new HashSet<int>();
    const float TurnTime = 20f; // 내 턴 제한 시간
    float turnEnd;
    int lastTick;
    List<Result> results;
    readonly System.Random rng = new System.Random();

    Font font;
    Camera cam;
    Material blackMat;
    Texture2D cardTex, backTex, panelTex, whiteTex;
    AudioSource sfx;
    AudioClip snapClip, passClip, turnClip, tickClip;

    // ---------- 장면 ----------

    void Start()
    {
        Application.targetFrameRate = 60;
        Application.runInBackground = true;
        font = Resources.Load<Font>("Fonts/NotoSansKR");
        myName = PlayerPrefs.GetString("name", "");
        BuildRoom();
        BuildTextures();
        BuildSounds();
        InitNet();
    }

    static Material Mat(Color c, float smooth = 0.2f, Color? emission = null)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", smooth);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
        }
        return m;
    }

    GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Transform parent = null)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    void BuildRoom()
    {
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) Destroy(c.gameObject);
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) Destroy(l.gameObject);

        var bg = new Color(0.035f, 0.03f, 0.045f);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.045f, 0.06f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.18f;
        RenderSettings.fogColor = bg;

        cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bg;
        cam.transform.position = new Vector3(0, 1.85f, -2.25f);
        cam.transform.LookAt(new Vector3(0, 0.82f, 0.25f));
        cam.gameObject.AddComponent<AudioListener>();

        // 원탁: 짙은 초록 펠트 + 검은 나무 테두리
        var felt = Mat(new Color(0.06f, 0.3f, 0.16f), 0.05f);
        var wood = Mat(new Color(0.09f, 0.05f, 0.03f), 0.5f);
        Prim(PrimitiveType.Cylinder, new Vector3(0, 0.74f, 0), new Vector3(2.9f, 0.035f, 2.9f), wood);
        Prim(PrimitiveType.Cylinder, new Vector3(0, 0.765f, 0), new Vector3(2.7f, 0.02f, 2.7f), felt);
        Prim(PrimitiveType.Cylinder, new Vector3(0, 0.37f, 0), new Vector3(0.35f, 0.37f, 0.35f), wood);

        // 테이블 위에 매달린 전등 하나만
        var lamp = new GameObject("Lamp").AddComponent<Light>();
        lamp.type = LightType.Spot;
        lamp.transform.position = new Vector3(0, 3.1f, 0);
        lamp.transform.rotation = Quaternion.Euler(90, 0, 0);
        lamp.spotAngle = 62;
        lamp.range = 7;
        lamp.intensity = 4.2f;
        lamp.color = new Color(1f, 0.82f, 0.55f);
        lamp.shadows = LightShadows.Soft;
        var shade = Mat(new Color(0.05f, 0.05f, 0.05f), 0.6f);
        // 매달린 공업용 전등 (Poly Haven). 그림자는 끔: 테이블 가운데가 가려지지 않게
        var lampModel = Model("hanging_industrial_lamp", new Vector3(0, 3.55f, 0), 0, 1f);
        if (lampModel != null)
            foreach (var r in lampModel.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lamp.transform.position = new Vector3(0, 2.15f, 0);

        // 어두운 구석: 와인 통 위 술병, 다른 통 위 촛불
        Model("wine_barrel_01", new Vector3(-2.7f, 0, 2.3f), 20, 1f);
        Model("wine_bottles_01", new Vector3(-2.55f, 0.87f, 2.3f), 30, 0.7f);
        Model("wine_barrel_01", new Vector3(2.8f, 0, 1.6f), -35, 1f);
        Model("brass_candleholders", new Vector3(2.8f, 0.87f, 1.6f), -60, 0.6f);
        candle = new GameObject("Candle").AddComponent<Light>();
        candle.type = LightType.Point;
        candle.transform.position = new Vector3(2.75f, 1.45f, 1.55f);
        candle.range = 2.6f;
        candle.color = new Color(1f, 0.6f, 0.25f);

        // 실루엣 뒤를 아주 희미하게 비추는 붉은빛 (사람 윤곽이 배경에서 떨어져 보이게)
        var back = new GameObject("BackGlow").AddComponent<Light>();
        back.type = LightType.Point;
        back.transform.position = new Vector3(0, 2.2f, 4.5f);
        back.range = 9;
        back.intensity = 0.9f;
        back.color = new Color(0.55f, 0.12f, 0.1f);

        blackMat = Mat(new Color(0.008f, 0.008f, 0.01f), 0.35f);
        for (int i = 0; i < 4; i++) seats[i] = MakeSeat();
    }

    Light candle;

    // Resources/PolyHaven/{id}/{id}_1k.fbx 를 놓는다 (없으면 null)
    GameObject Model(string id, Vector3 pos, float rotY, float scale, Transform parent = null)
    {
        var src = Resources.Load<GameObject>($"PolyHaven/{id}/{id}_1k");
        if (src == null) return null;
        var g = Instantiate(src, parent, false);
        g.transform.localPosition = pos;
        // FBX 원래 회전·배율(블렌더 단위 보정)을 살린 채로 돌리고 키운다
        g.transform.localRotation = Quaternion.Euler(0, rotY, 0) * src.transform.localRotation;
        g.transform.localScale = src.transform.localScale * scale;
        return g;
    }

    Seat MakeSeat()
    {
        var s = new Seat { root = new GameObject("Seat") };
        var t = s.root.transform;
        Model("WoodenChair_01", new Vector3(0, 0, -0.42f), 0, 0.46f, t);
        Prim(PrimitiveType.Capsule, new Vector3(0, 0.9f, -0.05f), new Vector3(0.6f, 0.36f, 0.36f), blackMat, t); // 몸통
        // 머리·어깨는 대리석 흉상을 새까맣게 칠해서 실루엣으로
        var bust = Model("marble_bust_01", new Vector3(0, 0.95f, -0.05f), -90, 2.0f, t); // 원본은 옆을 봄
        if (bust != null)
            foreach (var r in bust.GetComponentsInChildren<Renderer>()) r.sharedMaterial = blackMat;
        else Prim(PrimitiveType.Sphere, new Vector3(0, 1.62f, 0), new Vector3(0.3f, 0.36f, 0.32f), blackMat, t);
        Model("cigarette_pack", new Vector3(0.38f, 0.775f, 0.55f), 25, 1.2f, t);

        s.halo = new GameObject("TurnLight").AddComponent<Light>();
        s.halo.transform.SetParent(t, false);
        s.halo.transform.localPosition = new Vector3(0, 1.9f, -0.5f);
        s.halo.type = LightType.Point;
        s.halo.range = 1.6f;
        s.halo.intensity = 0;
        s.halo.color = new Color(1f, 0.35f, 0.2f);

        var lg = new GameObject("Label");
        lg.transform.SetParent(t, false);
        lg.transform.localPosition = new Vector3(0, 1.18f, 0.26f); // 가슴 위 (접속한 사람 이름)
        s.label = lg.AddComponent<TextMesh>();
        s.label.font = font;
        s.label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        s.label.fontSize = 64;
        s.label.characterSize = 0.018f;
        s.label.anchor = TextAnchor.MiddleCenter;
        s.label.alignment = TextAlignment.Center;

        s.pile = new GameObject("Backs").transform;
        s.pile.SetParent(t, false);
        s.pile.localPosition = new Vector3(0, 0.78f, 0.62f);
        s.root.SetActive(false);
        return s;
    }

    // 상대 자리: 나는 화면 아래(180°), 다음 차례는 내 왼쪽부터 시계 방향
    void PlaceSeats()
    {
        foreach (var s in seats) s.root.SetActive(false);
        for (int k = 1; k < numPlayers; k++)
        {
            var s = seats[k];
            float a = (180f + k * 360f / numPlayers) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            s.root.transform.position = dir * 1.75f;
            s.root.transform.rotation = Quaternion.LookRotation(-dir);
            s.root.SetActive(true);
            players[k].seat = s;
        }
        // 세로 화면(폰)에서는 양옆 사람이 보이게 시야를 넓힌다
        cam.fieldOfView = cam.aspect < 1f ? 92f : 60f;
    }

    void RefreshSeats()
    {
        Sync(); // 온라인 방장이면 바뀐 판을 참가자들에게 보냄
        if (pileStack == null)
        {
            pileStack = Prim(PrimitiveType.Cube, Vector3.zero, Vector3.one, backMat).transform;
            pileStack.rotation = Quaternion.Euler(0, 18, 0);
        }
        float hgt = Mathf.Max(0.002f, drawPile.Count * 0.0016f);
        pileStack.gameObject.SetActive(drawPile.Count > 0);
        pileStack.position = new Vector3(0.72f, 0.775f + hgt / 2, -0.15f);
        pileStack.localScale = new Vector3(0.13f, hgt, 0.19f);
        for (int k = 1; k < players.Count; k++)
        {
            var p = players[k];
            var s = p.seat;
            s.label.text = $"{p.name}\n<size=48>{p.hand.Count}장</size>";
            s.label.color = (phase == Phase.Play && turn == k) ? new Color(1f, 0.55f, 0.35f) : new Color(0.75f, 0.72f, 0.68f);
            // 손에 든 카드 뒷면을 테이블 위에 부채꼴로 (최대 13장까지만 그림)
            int show = Mathf.Min(p.hand.Count, 13);
            while (s.pile.childCount > show) DestroyImmediate(s.pile.GetChild(s.pile.childCount - 1).gameObject);
            while (s.pile.childCount < show)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(s.pile, false);
                q.GetComponent<Renderer>().sharedMaterial = backMat;
            }
            for (int i = 0; i < show; i++)
            {
                var c = s.pile.GetChild(i);
                float off = (i - (show - 1) / 2f);
                c.localPosition = new Vector3(off * 0.045f, 0.001f + i * 0.0008f, -Mathf.Abs(off) * 0.006f);
                c.localRotation = Quaternion.Euler(90, 0, off * 4f);
                c.localScale = new Vector3(0.13f, 0.19f, 1);
            }
        }
    }

    Material backMat;

    void BuildTextures()
    {
        whiteTex = Texture2D.whiteTexture;
        cardTex = Rounded(96, 136, 10, new Color(0.96f, 0.94f, 0.88f), new Color(0.55f, 0.5f, 0.45f));
        backTex = Rounded(96, 136, 10, new Color(0.38f, 0.05f, 0.06f), new Color(0.85f, 0.7f, 0.45f), true);
        panelTex = Rounded(64, 64, 14, new Color(0, 0, 0, 0.78f), new Color(0.6f, 0.25f, 0.15f, 0.8f));
        backMat = Mat(Color.white, 0.3f);
        backMat.mainTexture = Rounded(96, 136, 0, new Color(0.38f, 0.05f, 0.06f), new Color(0.85f, 0.7f, 0.45f), true);
    }

    static Texture2D Rounded(int w, int h, int r, Color fill, Color edge, bool pattern = false)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(r - x, x - (w - 1 - r), 0), dy = Mathf.Max(r - y, y - (h - 1 - r), 0);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float inside = Mathf.Clamp01(r - d + 0.5f);
                bool border = d > r - 2.5f || x < 2 || y < 2 || x > w - 3 || y > h - 3;
                var c = border ? edge : fill;
                if (pattern && !border)
                {
                    bool inner = x > 8 && y > 8 && x < w - 9 && y < h - 9;
                    if (!inner) c = fill * 0.7f;
                    else if (((x + y) / 6 + (x - y + 1000) / 6) % 2 == 0) c = Color.Lerp(fill, edge, 0.25f);
                    c.a = 1;
                }
                c.a *= inside;
                px[y * w + x] = c;
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    // 효과음도 코드로: 카드 내려놓는 소리, 패스, 내 차례 심장 소리
    void BuildSounds()
    {
        sfx = gameObject.AddComponent<AudioSource>();
        // 카드가 펠트에 "탁" 내려앉는 소리: 낮은 쿵 + 부드럽게 깎은 종이 소리
        float lp = 0;
        snapClip = Synth(0.12f, (t, i) =>
        {
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.35f;
            return (Mathf.Sin(t * 2 * Mathf.PI * 120f) * Mathf.Exp(-t * 45f) * 0.55f + lp * Mathf.Exp(-t * 70f) * 0.5f);
        });
        tickClip = Synth(0.05f, (t, i) => Mathf.Sin(t * 2 * Mathf.PI * 900f) * Mathf.Exp(-t * 90f) * 0.25f);
        passClip = Synth(0.2f, (t, i) => // 손가락으로 테이블 두 번 톡톡
            (Mathf.Sin(t * 2 * Mathf.PI * 95f) * Mathf.Exp(-t * 60f) + (t > 0.11f ? Mathf.Sin((t - 0.11f) * 2 * Mathf.PI * 95f) * Mathf.Exp(-(t - 0.11f) * 60f) : 0)) * 0.35f);
        turnClip = Synth(0.5f, (t, i) =>
        {
            float b1 = Mathf.Exp(-t * 25f), b2 = t > 0.22f ? Mathf.Exp(-(t - 0.22f) * 25f) : 0;
            return Mathf.Sin(t * 2 * Mathf.PI * 55f) * (b1 + b2 * 0.8f) * 0.7f;
        });

        // 배경: 아주 작게 깔리는 방 공기 소리 (깎은 갈색 잡음). 끝과 처음이 이어지게 기울기를 뺀다
        int rate = 22050, n = rate * 8;
        var room = new float[n];
        float w = 0, w2 = 0;
        for (int i = 0; i < n; i++)
        {
            w = Mathf.Clamp(w + (float)(rng.NextDouble() * 2 - 1) * 0.02f, -1, 1);
            w2 += (w - w2) * 0.05f;
            room[i] = w2;
        }
        float drift = room[n - 1] - room[0];
        for (int i = 0; i < n; i++) room[i] = (room[i] - drift * i / n) * 0.5f;
        var amb = gameObject.AddComponent<AudioSource>();
        amb.clip = AudioClip.Create("room", n, 1, rate, false);
        amb.clip.SetData(room, 0);
        amb.loop = true;
        amb.volume = 0.35f;
        amb.Play();
    }

    static AudioClip Synth(float len, System.Func<float, int, float> f)
    {
        int rate = 22050, n = (int)(len * rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = f(i / (float)rate, i);
        var clip = AudioClip.Create("sfx", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------- 진행 ----------

    void NewGame()
    {
        players.Clear();
        players.Add(new Player { name = string.IsNullOrWhiteSpace(myName) ? "나" : myName.Trim() });
        PlayerPrefs.SetString("name", myName);
        string[] botNames = { "검은 모자", "말 없는 자", "그림자" };
        for (int i = 1; i < numPlayers; i++) players.Add(new Player { name = botNames[i - 1], bot = true });
        round = 0;
        PlaceSeats();
        StartRound();
    }

    void StartRound()
    {
        round++;
        var deck = Rules.MakeDeck(packs);
        Rules.Shuffle(deck, rng);
        for (int i = 0; i < numPlayers; i++)
            players[i].hand = deck.Skip(i * DealCount).Take(DealCount).OrderBy(c => c.Val).ToList();
        drawPile = deck.Skip(numPlayers * DealCount).ToList();

        // 가장 낮은 카드(보통 ♦3)를 가진 사람이 먼저
        int first = 0, best = int.MaxValue;
        for (int i = 0; i < numPlayers; i++)
        {
            int v = players[i].hand.Min(c => c.Val);
            if (v < best) { best = v; first = i; }
        }
        lastCombo = null; lastCards.Clear(); lastBy = -1; passes = 0;
        selected.Clear();
        results = null;
        phase = Phase.Play;
        lastText = $"{players[first].name} 먼저 시작 (가장 낮은 카드)";
        SetTurn(first);
    }

    void SetTurn(int i)
    {
        turn = i;
        botTimer = Random.Range(0.9f, 1.8f);
        for (int k = 1; k < players.Count; k++) players[k].seat.halo.intensity = 0;
        if (i > 0) players[i].seat.halo.intensity = 2.2f;
        else
        {
            sfx.PlayOneShot(turnClip, 0.9f);
            playable = Rules.Playable(players[0].hand, lastCombo);
        }
        if (!players[i].bot) { turnEnd = Time.time + TurnTime; lastTick = 99; }
        RefreshSeats();
    }

    int Next(int i) => (i + 1) % numPlayers;

    bool Play(int i, List<Card> cards)
    {
        var combo = Rules.Eval(cards);
        if (combo == null) { Toast("낼 수 없는 조합이에요"); return false; }
        if (lastCombo != null && !combo.Beats(lastCombo))
        {
            Toast(combo.type != lastCombo.type ? $"{lastCombo.name}에는 같은 장수로만 낼 수 있어요" : "더 높은 패를 내야 해요");
            return false;
        }
        var p = players[i];
        foreach (var c in cards) p.hand.Remove(c);
        lastCombo = combo;
        lastCards = cards.OrderBy(c => c.Val).ToList();
        lastBy = i;
        passes = 0;
        lastText = $"{p.name} · {combo.name}";
        OnCardsPlayed(i, cards.Count);
        if (p.hand.Count == 0) { EndRound(i); return true; }
        SetTurn(Next(i));
        return true;
    }

    void Pass(int i)
    {
        if (lastCombo == null) return;
        passes++;
        sfx.PlayOneShot(passClip, 0.7f);
        string drew = "";
        if (drawPile.Count > 0)
        {
            var p = players[i];
            p.hand.Add(drawPile[0]);
            drawPile.RemoveAt(0);
            p.hand = p.hand.OrderBy(c => c.Val).ToList();
            drew = " · 1장 먹음";
            if (i == 0) Toast("패스 · 카드 1장 먹음");
        }
        if (passes >= numPlayers - 1)
        {
            // 모두 패스 → 마지막에 낸 사람이 새로 시작
            lastCombo = null;
            lastCards.Clear();
            passes = 0;
            lastText = $"{players[i].name} 패스{drew} · 모두 패스 → {players[lastBy].name} 새로 시작";
            SetTurn(lastBy);
        }
        else
        {
            lastText = $"{players[i].name} 패스{drew}";
            SetTurn(Next(i));
        }
    }

    void EndRound(int winner)
    {
        phase = Phase.End;
        results = players.Select(p => new Result
        {
            p = p,
            left = p.hand.Count,
            heavy = p.hand.Count(c => c.Heavy),
            score = Rules.Score(p.hand),
        }).OrderBy(r => r.score).ThenBy(r => r.left).ToList();
        foreach (var r in results) r.p.total += r.score;
        for (int k = 1; k < players.Count; k++) players[k].seat.halo.intensity = 0;
        RefreshSeats();
    }

    void Update()
    {
        if (mode == NetMode.Client || phase != Phase.Play) return;
        if (!players[turn].bot) { HumanClock(); return; }
        botTimer -= Time.deltaTime;
        if (botTimer > 0) return;
        var move = Rules.BotMove(players[turn].hand, lastCombo);
        if (move == null || !Play(turn, move)) Pass(turn);
    }

    // 내 턴 시간: 마지막 5초는 똑딱, 다 되면 자동 패스 (판을 여는 차례면 가장 약한 패를 냄)
    void HumanClock()
    {
        float left = turnEnd - Time.time;
        int sec = Mathf.CeilToInt(left);
        if (turn == 0 && sec <= 5 && sec < lastTick && sec > 0) { lastTick = sec; sfx.PlayOneShot(tickClip, 0.6f); }
        if (left > 0) return;
        if (turn == 0) { selected.Clear(); Toast("시간 초과"); }
        if (lastCombo == null) Play(turn, Rules.BotMove(players[turn].hand, null));
        else Pass(turn);
    }

    // ---------- 카드 내는 연출 ----------
    float landAt; // 가운데 카드가 테이블에 닿는 시각

    void OnCardsPlayed(int by, int count)
    {
        float fly = by > 0 && by < players.Count ? 0.38f : 0.08f;
        landAt = Time.time + fly;
        StartCoroutine(PlayCardsFx(by, count, fly));
    }

    System.Collections.IEnumerator PlayCardsFx(int by, int count, float fly)
    {
        var flying = new List<Transform>();
        if (by > 0 && by < players.Count)
        {
            // 상대 자리에서 뒷면 카드가 테이블 가운데로 미끄러져 온다
            var from = players[by].seat.pile.position + Vector3.up * 0.05f;
            for (int k = 0; k < count; k++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.GetComponent<Renderer>().sharedMaterial = backMat;
                q.transform.localScale = new Vector3(0.13f, 0.19f, 1);
                flying.Add(q.transform);
            }
            var to = new Vector3(0, 0.79f, 0.15f);
            for (float t = 0; t < 1; t += Time.deltaTime / fly)
            {
                float e = 1 - (1 - t) * (1 - t);
                for (int k = 0; k < flying.Count; k++)
                {
                    var off = new Vector3((k - (count - 1) / 2f) * 0.06f * e, 0, 0);
                    flying[k].position = Vector3.Lerp(from, to, e) + off + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.12f;
                    flying[k].rotation = Quaternion.Euler(90, Mathf.Lerp(-40, 0, e) + k * 6, 0);
                }
                yield return null;
            }
        }
        else yield return new WaitForSeconds(fly);
        // 한 장씩 착착 내려앉는 소리
        for (int k = 0; k < count; k++)
        {
            sfx.pitch = 0.92f + (float)rng.NextDouble() * 0.16f;
            sfx.PlayOneShot(snapClip, 0.55f);
            yield return new WaitForSeconds(0.045f);
        }
        sfx.pitch = 1;
        yield return new WaitForSeconds(0.1f);
        foreach (var f in flying) Destroy(f.gameObject);
    }

    void LateUpdate()
    {
        // 말하는 사람 머리 위 조명 (온라인 마이크)
        for (int k = 1; k < players.Count; k++)
        {
            var p = players[k];
            if (p.seat == null || p.peer == null || !speak.TryGetValue(p.peer, out var lv)) continue;
            float baseI = phase == Phase.Play && turn == k ? 2.2f : 0;
            p.seat.halo.intensity = baseI + Mathf.Clamp01(lv * 8f) * 2.5f;
        }
        if (candle != null) candle.intensity = 1.1f + Mathf.PerlinNoise(Time.time * 7f, 0) * 0.8f; // 촛불 흔들림
        // 이름표는 항상 카메라를 향하게
        foreach (var s in seats)
            if (s.root.activeSelf) s.label.transform.rotation = Quaternion.LookRotation(s.label.transform.position - cam.transform.position);
    }

    void Toast(string msg) { toast = msg; toastUntil = Time.time + 2f; }

    // ---------- 화면 (OnGUI, 가상 높이 1000 기준) ----------

    float VW => Screen.width / (Screen.height / 1000f);
    GUIStyle Style(int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle fs = FontStyle.Normal)
        => new GUIStyle(GUI.skin.label) { font = font, fontSize = size, alignment = anchor, fontStyle = fs, normal = { textColor = color }, wordWrap = false, clipping = TextClipping.Overflow };

    bool Btn(Rect r, string text, bool on = true, bool accent = false)
    {
        var old = GUI.color;
        GUI.color = !on ? new Color(1, 1, 1, 0.35f) : accent ? new Color(1f, 0.6f, 0.45f) : Color.white;
        GUI.DrawTexture(r, panelTex, ScaleMode.StretchToFill);
        GUI.color = old;
        GUI.Label(r, text, Style(Mathf.RoundToInt(r.height * 0.42f), on ? new Color(0.95f, 0.9f, 0.82f) : new Color(0.6f, 0.6f, 0.6f)));
        return on && GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    void DrawCard(Rect r, Card c, float dim = 1f)
    {
        var old = GUI.color;
        GUI.color = new Color(dim, dim, dim);
        GUI.DrawTexture(r, cardTex, ScaleMode.StretchToFill);
        GUI.color = old;
        var col = c.Red ? new Color(0.75f, 0.08f, 0.1f) : new Color(0.08f, 0.08f, 0.1f);
        float h = r.height;
        if (c.Joker)
        {
            GUI.Label(new Rect(r.x + h * 0.05f, r.y + h * 0.03f, r.width, h * 0.16f), "JK", Style(Mathf.RoundToInt(h * 0.13f), col, TextAnchor.UpperLeft, FontStyle.Bold));
            GUI.Label(new Rect(r.x, r.y + h * 0.3f, r.width, h * 0.4f), "★", Style(Mathf.RoundToInt(h * 0.34f), col));
            GUI.Label(new Rect(r.x, r.y + h * 0.7f, r.width, h * 0.2f), "JOKER", Style(Mathf.RoundToInt(h * 0.1f), col, TextAnchor.MiddleCenter, FontStyle.Bold));
            return;
        }
        GUI.Label(new Rect(r.x + h * 0.05f, r.y + h * 0.02f, r.width, h * 0.2f), c.RankText, Style(Mathf.RoundToInt(h * 0.17f), col, TextAnchor.UpperLeft, FontStyle.Bold));
        GUI.Label(new Rect(r.x + h * 0.05f, r.y + h * 0.19f, r.width, h * 0.16f), c.SuitText, Style(Mathf.RoundToInt(h * 0.13f), col, TextAnchor.UpperLeft));
        GUI.Label(new Rect(r.x, r.y + h * 0.3f, r.width, h * 0.55f), c.SuitText, Style(Mathf.RoundToInt(h * 0.38f), col));
    }

    void OnGUI()
    {
        if (font == null) return;
        float sc = Screen.height / 1000f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1));
        float W = VW;

        if (phase == Phase.Menu) { DrawMenu(W); DrawToast(W); return; }
        if (phase == Phase.Lobby) { DrawLobby(W); DrawToast(W); return; }

        // 위쪽 상태 줄
        string status = phase == Phase.End ? "판 끝" : turn == 0 ? "내 차례" : $"{players[turn].name} 생각 중…";
        GUI.Label(new Rect(0, 18, W, 50), status, Style(40, turn == 0 && phase == Phase.Play ? new Color(1f, 0.6f, 0.4f) : new Color(0.8f, 0.76f, 0.7f), TextAnchor.MiddleCenter, FontStyle.Bold));
        GUI.Label(new Rect(0, 66, W, 34), $"{round}판 · 카드 {packs}팩 · {numPlayers}명 · 더미 {drawPile.Count}장", Style(22, new Color(0.55f, 0.52f, 0.5f)));

        // 가운데: 마지막에 낸 패
        GUI.Label(new Rect(0, 400, W, 40), lastText, Style(28, new Color(0.9f, 0.85f, 0.75f)));
        if (lastCards.Count > 0)
        {
            float ch = 130, cw = ch * 0.7f, gap = cw * 0.55f;
            float total = cw + gap * (lastCards.Count - 1);
            float since = Time.time - landAt;
            if (since >= 0)
            {
                float k = Mathf.Clamp01(since / 0.15f), sc2 = Mathf.Lerp(1.22f, 1f, k);
                for (int i = 0; i < lastCards.Count; i++)
                {
                    var r = new Rect(W / 2 - total / 2 + i * gap, 440, cw, ch);
                    DrawCard(new Rect(r.center.x - r.width * sc2 / 2, r.center.y - r.height * sc2 / 2 - (1 - k) * 18, r.width * sc2, r.height * sc2), lastCards[i]);
                }
            }
        }

        DrawHand(W);

        if (phase == Phase.Play && turn == 0)
        {
            var sel = players[0].hand.Where(c => selected.Contains(c.id)).ToList();
            bool canPass = lastCombo != null;
            if (Btn(new Rect(W / 2 + 20, 612, 200, 68), "내기", sel.Count > 0, true))
            {
                if (mode == NetMode.Client)
                {
                    var cb = Rules.Eval(sel);
                    if (cb != null && (lastCombo == null || cb.Beats(lastCombo))) { SendAction("play", sel); selected.Clear(); }
                    else Toast("낼 수 없는 패예요");
                }
                else if (Play(0, sel)) selected.Clear();
            }
            if (Btn(new Rect(W / 2 - 220, 612, 200, 68), "패스", canPass))
            {
                selected.Clear();
                if (mode == NetMode.Client) SendAction("pass"); else Pass(0);
            }
            if (lastCombo == null && sel.Count == 0) GUI.Label(new Rect(0, 470, W, 34), "아무 조합이나 낼 수 있어요", Style(22, new Color(0.7f, 0.68f, 0.62f)));
            if (lastCombo != null && playable.Count == 0) GUI.Label(new Rect(0, 574, W, 34), "낼 수 있는 패가 없어요 · 패스", Style(24, new Color(0.75f, 0.7f, 0.65f)));

            // 고른 패 미리보기
            if (sel.Count > 0)
            {
                var cb = Rules.Eval(sel);
                bool good = cb != null && (lastCombo == null || cb.Beats(lastCombo));
                string msg = cb == null ? "안 되는 조합이에요"
                    : lastCombo != null && cb.type != lastCombo.type ? $"{lastCombo.name}에는 {lastCombo.type}장으로 내야 해요"
                    : good ? $"{cb.name} · 낼 수 있어요" : $"{cb.name} · 더 높아야 해요";
                GUI.Label(new Rect(0, 574, W, 34), msg, Style(24, good ? new Color(0.55f, 0.95f, 0.6f) : new Color(1f, 0.5f, 0.4f), TextAnchor.MiddleCenter, FontStyle.Bold));
            }

            if (Btn(new Rect(W / 2 + 240, 612, 150, 68), "추천"))
            {
                var hint = Rules.BotMove(players[0].hand, lastCombo);
                selected.Clear();
                if (hint == null) Toast("낼 수 있는 패가 없어요 · 패스");
                else foreach (var c in hint) selected.Add(c.id);
            }

            // 남은 시간 막대
            float left = Mathf.Max(0, turnEnd - Time.time), bw = Mathf.Min(420, W - 80);
            var urgent = left <= 5f;
            GUI.color = new Color(1, 1, 1, 0.12f);
            GUI.DrawTexture(new Rect(W / 2 - bw / 2, 108, bw, 8), whiteTex);
            GUI.color = urgent ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.6f, 0.4f);
            GUI.DrawTexture(new Rect(W / 2 - bw / 2, 108, bw * left / TurnTime, 8), whiteTex);
            GUI.color = Color.white;
            GUI.Label(new Rect(W / 2 + bw / 2 + 8, 96, 80, 32), $"{Mathf.CeilToInt(left)}초", Style(22, urgent ? new Color(1f, 0.35f, 0.3f) : new Color(0.75f, 0.7f, 0.65f), TextAnchor.MiddleLeft));
        }

        DrawToast(W);

        if (mode != NetMode.Solo && Btn(new Rect(20, 18, 170, 50), micOn ? "마이크 끄기" : "마이크 켜기", true, micOn)) NetMic(!micOn);
        if (phase == Phase.End) DrawEnd(W);
        else if (Btn(new Rect(W - 130, 18, 110, 50), "그만")) LeaveRoom();
    }

    void DrawToast(float W)
    {
        if (Time.time < toastUntil)
            GUI.Label(new Rect(0, 380, W, 40), toast, Style(28, new Color(1f, 0.45f, 0.35f), TextAnchor.MiddleCenter, FontStyle.Bold));
    }

    void DrawHand(float W)
    {
        var hand = players[0].hand;
        int n = hand.Count;
        if (n == 0) return;
        float ch = 200, cw = ch * 0.7f;
        float avail = W - 40;
        float step = n > 1 ? Mathf.Min(cw * 0.62f, (avail - cw) / (n - 1)) : 0;
        float x0 = W / 2 - (cw + step * (n - 1)) / 2;
        var rects = new Rect[n];
        for (int i = 0; i < n; i++)
            rects[i] = new Rect(x0 + i * step, 1000 - ch - 24 - (selected.Contains(hand[i].id) ? 40 : 0), cw, ch);

        // 겹친 카드는 위에 그려진(오른쪽) 카드가 눌린다
        var e = Event.current;
        if (phase == Phase.Play && e.type == EventType.MouseDown)
            for (int i = n - 1; i >= 0; i--)
            {
                var hit = rects[i];
                hit.height += 40;
                if (!hit.Contains(e.mousePosition)) continue;
                int id = hand[i].id;
                if (!selected.Remove(id)) selected.Add(id);
                e.Use();
                break;
            }

                for (int i = 0; i < n; i++)
        {
            float dim = phase != Phase.Play ? 1f : turn != 0 ? 0.78f : playable.Contains(hand[i].id) ? 1f : 0.42f;
            DrawCard(rects[i], hand[i], dim);
            if (phase == Phase.Play && turn == 0 && lastCombo != null && playable.Contains(hand[i].id))
            {
                var old = GUI.color; // 낼 수 있는 카드 아래 주황 띠
                GUI.color = new Color(1f, 0.55f, 0.3f, 0.9f);
                GUI.DrawTexture(new Rect(rects[i].x + 6, rects[i].yMax - 7, Mathf.Min(step, cw) - 12, 5), whiteTex);
                GUI.color = old;
            }
        }
    }

    void DrawMenu(float W)
    {
        var dim = new Color(0.7f, 0.66f, 0.6f);
        GUI.Label(new Rect(0, 150, W, 120), "BIG TWO", Style(96, new Color(0.95f, 0.55f, 0.4f), TextAnchor.MiddleCenter, FontStyle.Bold));
        GUI.Label(new Rect(0, 265, W, 40), "빅 투 · 어두운 방의 원탁", Style(28, dim));

        float bw = 150, bh = 76, cx = W / 2;
        GUI.Label(new Rect(cx - 330, 318, 160, 50), "내 이름", Style(28, dim, TextAnchor.MiddleRight));
        var field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 28, alignment = TextAnchor.MiddleCenter, normal = { background = panelTex, textColor = Color.white }, focused = { background = panelTex, textColor = Color.white } };
        if (Online) { if (Btn(new Rect(cx - 150, 318, 300, 50), string.IsNullOrWhiteSpace(myName) ? "눌러서 입력" : myName)) NetAsk("name", "내 이름 (8자까지)", myName ?? ""); }
        else myName = GUI.TextField(new Rect(cx - 150, 318, 300, 50), myName ?? "", 8, field);
        GUI.Label(new Rect(0, 372, W, 34), "봇과 연습할 때 인원", Style(26, dim));
        for (int k = 2; k <= 4; k++)
            if (Btn(new Rect(cx - bw * 1.5f - 15 + (k - 2) * (bw + 15), 410, bw, bh), $"{k}명", true, numPlayers == k)) numPlayers = k;

        GUI.Label(new Rect(0, 520, W, 40), "카드", Style(28, dim));
        for (int k = 1; k <= 2; k++)
            if (Btn(new Rect(cx - bw - 8 + (k - 1) * (bw + 15), 570, bw, bh), $"{k}팩", true, packs == k)) packs = k;
        GUI.Label(new Rect(0, 655, W, 34), packs == 1 ? "54장 (조커 2장 포함)" : "108장 (조커 4장 포함)", Style(22, new Color(0.5f, 0.48f, 0.45f)));

        // 혼자 연습 / 친구와 온라인 방
        if (Btn(new Rect(cx - 400, 730, 250, 90), "봇과 연습")) NewGame();
        if (Btn(new Rect(cx - 125, 730, 250, 90), "방 만들기", true, inviteCode == "")) CreateRoom();
        if (Btn(new Rect(cx + 150, 730, 250, 90), inviteCode != "" ? $"{inviteCode} 참가" : "코드로 참가", true, inviteCode != ""))
            JoinRoom(inviteCode);
        if (inviteCode != "") GUI.Label(new Rect(0, 690, W, 34), $"초대받은 방: {inviteCode}", Style(24, new Color(1f, 0.6f, 0.4f)));
        GUI.Label(new Rect(0, 860, W, 80), "9장씩 시작 · 패스하면 더미에서 1장 먹기 · 가장 먼저 다 내면 1등\n나머지는 남은 카드 수로 순위 · 남은 2나 조커 한 장마다 점수 ×2 (점수 낮을수록 좋음)", Style(22, new Color(0.5f, 0.48f, 0.45f)));
    }

    void DrawEnd(float W)
    {
        float pw = Mathf.Min(760, W - 40), ph = 120 + results.Count * 74 + 110;
        var r = new Rect(W / 2 - pw / 2, 150, pw, ph);
        GUI.DrawTexture(r, panelTex, ScaleMode.StretchToFill);
        GUI.Label(new Rect(r.x, r.y + 20, pw, 60), results[0].p == players[0] ? "1등!" : $"{results[0].p.name} 1등", Style(52, new Color(1f, 0.6f, 0.4f), TextAnchor.MiddleCenter, FontStyle.Bold));
        for (int i = 0; i < results.Count; i++)
        {
            var x = results[i];
            float y = r.y + 100 + i * 74;
            string detail = x.left == 0 ? "다 냄" : x.heavy > 0 ? $"{x.left}장 × {1 << x.heavy} (2·조커 {x.heavy}장)" : $"{x.left}장";
            GUI.Label(new Rect(r.x + 30, y, 90, 60), $"{i + 1}등", Style(32, new Color(0.95f, 0.85f, 0.7f), TextAnchor.MiddleLeft, FontStyle.Bold));
            GUI.Label(new Rect(r.x + 120, y, 220, 60), x.p.name, Style(30, Color.white, TextAnchor.MiddleLeft));
            GUI.Label(new Rect(r.x + 300, y, pw - 330, 30), $"{detail} → {x.score}점", Style(24, new Color(0.85f, 0.8f, 0.72f), TextAnchor.MiddleRight));
            GUI.Label(new Rect(r.x + 300, y + 30, pw - 330, 28), $"누적 {x.p.total}점", Style(20, new Color(0.55f, 0.52f, 0.5f), TextAnchor.MiddleRight));
        }
        float by = r.yMax - 95;
        if (mode == NetMode.Client) GUI.Label(new Rect(W / 2 + 10, by, 220, 76), "방장이 다음 판을\n시작하길 기다려요", Style(22, new Color(0.75f, 0.7f, 0.65f)));
        else if (Btn(new Rect(W / 2 + 10, by, 220, 76), "다음 판", true, true)) StartRound();
        if (Btn(new Rect(W / 2 - 230, by, 220, 76), "나가기")) LeaveRoom();
    }
}
