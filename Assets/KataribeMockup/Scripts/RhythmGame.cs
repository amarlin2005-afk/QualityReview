using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 語り部リズムゲーム：ゲーム性モックアップ
///
/// 2小節で1セット：
///   ① お手本の小節 … 紙は止まったまま。青の羽ペン（と水色の小節線）が黒い線から右へ進み、ノートを書いていく
///   ② プレイの小節 … 書かれたノートと青の羽ペンが左へ流れ、黄色（プレイヤー）が黒い線でなぞる。
///                     小節の終わりに水色の線が黒い線に重なって合流 → また①へ
///
/// 操作：左スティックのY＝黄色の羽ペンの高さ（連続）／A＝ノート（ロングは押しっぱなし）／Start＝リスタート
/// キーボード：W/S（↑/↓）＝高さ、Space＝A、R＝リスタート
///
/// 使い方：空の GameObject にこのスクリプトを付けて再生するだけ（カメラや線は自動で作る）
/// </summary>
public class RhythmGame : MonoBehaviour
{
    enum Judge { Perfect, Good, Miss }

    class NoteView
    {
        public NoteData data;
        public GameObject go;
        public LineRenderer outer, inner;
        public float[] sampleBeats;
        public Vector3[] samplePts;
        public double measureStart, hitTime, endTime;
        public bool judged, fullyDrawn;
        public float holdOk, holdTotal;
    }

    class MeasureGroup
    {
        public int index;          // 通し番号（ループしても増え続ける）＝紙の上の位置（小節単位）
        public float maxBeat;      // いちばん後ろのノートの拍（3を超えると次の小節にはみ出す）
        public Transform root;
        public List<NoteView> notes = new List<NoteView>();
    }

    [Header("譜面（空なら内蔵のサンプル譜面）")]
    public TextAsset chartFile;
    public bool loop = true;

    [Header("曲（空ならメトロノームだけ）")]
    public AudioClip music;
    [Tooltip("曲ファイルの中で、最初の小節の1拍目が鳴る時間（秒）。ズレていたらここで合わせる")]
    public float firstBeatTime = 0.02f;
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Tooltip("曲と一緒にメトロノームも鳴らす（ズレの確認用）")]
    public bool metronomeWithMusic = false;

    [Header("テンポ")]
    public float bpm = 160f;
    public int beatsPerMeasure = 3;

    [Header("レイアウト（ワールド座標・スケッチに合わせた値）")]
    public float blackLineX = -5.8f;
    public float measureLength = 11.5f;   // 黒い線 → 水色の線 の距離 = 1小節
    public float playYMin = -3.4f;        // 高さ 0.0 の位置
    public float playYMax = -0.1f;        // 高さ 1.0 の位置
    public float paperTopY = 0.5f;
    public float paperBottomY = -4.0f;

    [Header("判定（秒 / 高さは 0〜1 の差）")]
    public float perfectTime = 0.06f;
    public float goodTime = 0.13f;
    public float perfectHeight = 0.10f;
    public float goodHeight = 0.20f;
    public float holdTolerance = 0.15f;
    [Range(0f, 1f)] public float holdPerfectRatio = 0.9f;
    [Range(0f, 1f)] public float holdGoodRatio = 0.6f;
    [Tooltip("押すのが遅れて判定される場合はプラスに")]
    public float inputOffsetMs = 0f;

    public enum StickMode { Velocity, Position, Delta }

    [Header("操作")]
    [Tooltip("Delta＝倒した分だけ動いて戻る（倍率つき）／Velocity＝傾きで動く向きと速さ／Position＝傾き＝高さ")]
    public StickMode stickMode = StickMode.Delta;
    [Tooltip("Deltaモード：倒した量に対する動く量の倍率。1で中央→端に倒すと高さが半分ぶん動く、2で全体ぶん")]
    [Range(0.5f, 4f)] public float deltaGain = 1.5f;
    [Tooltip("Deltaモード：これより小さいスティックの揺れは無視する（スティックの値 -1〜1 に対して）")]
    [Range(0f, 0.1f)] public float deltaJitter = 0.03f;
    [Tooltip("速度モード：いっぱいに倒したときの速さ（1秒あたりの高さ。1.0で下から上まで1秒）")]
    public float penSpeed = 1.2f;
    [Tooltip("速度モード：倒し具合と速さのカーブ。1＝比例、2＝少し倒したときはよりゆっくり")]
    [Range(1f, 3f)] public float speedCurve = 1.5f;
    [Tooltip("速度モード：これより小さい傾きは「その場にとどまる」。これを超えた分だけ動く")]
    [Range(0f, 0.6f)] public float stickDeadzone = 0.25f;
    [Tooltip("速度モード：手を離したら真ん中へ戻る")]
    public bool returnToCenter = true;
    [Tooltip("速度モード：これより小さい傾きは「手を離している」とみなして真ん中へ戻す（スティックの静止時のずれより少し大きく）")]
    [Range(0f, 0.3f)] public float returnZone = 0.08f;
    [Tooltip("速度モード：真ん中へ戻るまでの時間の目安（秒）。大きいほどゆっくり戻る")]
    public float returnTime = 0.35f;
    public bool invertStick = false;
    [Tooltip("L / R（LB / RB）でもノートを叩ける")]
    public bool shoulderButtons = true;
    [Tooltip("ZL / ZR（LT / RT）でもノートを叩ける")]
    public bool triggerButtons = false;
    [Tooltip("対数カーブの強さ。0＝まっすぐ、4前後＝真ん中が敏感で端がゆるやか、10以上＝かなり強い")]
    [Range(0f, 20f)] public float stickLogCurve = 4f;
    [Tooltip("これより小さいスティックの揺れは無視する（高さ0〜1に対して）。0.02〜0.05くらい")]
    [Range(0f, 0.1f)] public float jitterFilter = 0.03f;
    [Tooltip("羽ペンがスティックに追いつくまでの時間（秒）。0でそのまま、0.08前後でぬるっと、0.15以上でふわっと")]
    [Range(0f, 0.3f)] public float stickSmoothing = 0.1f;
    [Tooltip("羽ペンの最高速度（1秒で動ける高さ。0で無制限）。小さくすると重たい筆の感じに")]
    public float maxPenSpeed = 0f;

    [Header("ロングノートの吸着")]
    [Tooltip("Aを押していて、線と逆向きに倒していなければ、線に完全に吸い付いて追従する")]
    public bool longNoteMagnet = true;
    [Tooltip("オン：Aを押していれば、距離や向きに関係なく線を自動で追いかける（離すまで外れない）")]
    public bool longAutoFollow = true;
    [Tooltip("自動追従のとき、線が上がる所は上、下がる所は下にスティックを倒している間だけ追いかける")]
    public bool longRequireDirection = true;
    [Tooltip("「倒している」とみなすスティックの傾き（向きの判定だけに使う）")]
    [Range(0.05f, 0.6f)] public float directionThreshold = 0.15f;
    [Tooltip("向きが合わないフレームがこの数を超えたら自動追従が外れる（60fpsなら 15＝0.25秒）")]
    [Range(0, 60)] public int longGraceFrames = 15;
    [Tooltip("坂の向きが切り替わる前後、何拍ぶんは「次の向き／前の向き」に倒していてもOKにするか")]
    [Range(0f, 1f)] public float directionSwitchWindow = 0.25f;
    [Tooltip("この距離まで近づくと吸い付く（高さ0〜1の差）")]
    [Range(0f, 0.5f)] public float magnetRange = 0.2f;
    [Tooltip("線が「平ら」とみなす傾き（1拍あたりの高さ変化）。平らな所はスティックを大きく外へ倒さなければOK")]
    public float flatSlope = 0.15f;
    [Tooltip("逆向きに倒しても、この秒数までは吸着を保つ")]
    public float magnetGrace = 0.15f;
    [Tooltip("吸い付くまでの時間（秒）。0.05〜0.15くらい。吸い付いた後は線に完全に乗って動く")]
    public float magnetSnapTime = 0.1f;
    [Tooltip("オン：一度吸い付いたら、Aを離すまで線から外れない／オフ：向きが逆のままだと外れる")]
    public bool magnetKeepUntilRelease = true;

    [Header("ノートの高さへの吸着（タップ／ロングの始点）")]
    [Tooltip("次に来るノートの高さに近づくと、その高さに吸い付いて止まる")]
    public bool noteMagnet = true;
    [Tooltip("この距離まで近づくと吸い付く（高さ0〜1の差）")]
    [Range(0f, 1f)] public float noteMagnetRange = 0.3f;
    [Tooltip("これより強くスティックを倒すと吸着から抜ける（0〜1。Velocityモードでは閾値の外側の倒し具合）")]
    [Range(0.1f, 1f)] public float noteMagnetBreak = 0.9f;
    [Tooltip("強く倒し続けてから抜けるまでの時間（秒）。長いほど吸着が強い")]
    public float noteMagnetBreakTime = 0.25f;
    [Tooltip("次のノートから遠いとき、そこへ向かう速さを最大で何倍にするか（Velocityモード）")]
    [Range(1f, 5f)] public float farSpeedBoost = 2.5f;
    [Tooltip("ノートの時間までに届くよう、必要なら自動で速くする（Velocityモード）")]
    public bool arriveInTime = true;
    [Tooltip("吸い付く速さ（秒）。小さいほどピタッと")]
    public float noteMagnetSnapTime = 0.06f;
    [Tooltip("何秒先のノートまで吸着の対象にするか")]
    public float noteMagnetLookahead = 1.5f;

    [Header("見た目")]
    public Color inkColor = new Color(0.02f, 0.05f, 0.02f);
    public Color paperColor = Color.white;
    public Color playerColor = new Color(0.88f, 0.65f, 0.15f);
    public Color narratorColor = new Color(0.08f, 0.25f, 0.50f);
    public Color barLineColor = new Color(0.35f, 0.65f, 1f);
    public Color perfectColor = new Color(0.95f, 0.68f, 0.10f);
    public Color goodColor = new Color(0.98f, 0.84f, 0.50f);
    public Color missColor = new Color(0.72f, 0.72f, 0.72f);
    public float tapRadius = 0.32f;
    public float longWidth = 0.55f;

    // --- 状態 ---
    List<MeasureData> measures;
    readonly List<MeasureGroup> groups = new List<MeasureGroup>();
    Metronome metronome;
    AudioSource musicSource;
    double musicEndTime = double.MaxValue;
    Transform notesRoot;
    Transform yellowPen, bluePen, blueLine;
    readonly List<Vector3> buffer = new List<Vector3>();

    double songStartDsp, songTime, prevSongTime;
    // 進行表（譜面1周ぶん。小節ごとに「お手本の開始」と「プレイの開始」）
    double[] demoOff, playOff;
    double loopLen;
    int nextGroupIndex;
    readonly List<Vector2> guideKeys = new List<Vector2>();   // 青の羽ペンの道筋（x＝通しの拍）
    float paperScroll, penPaper, penH = 0.5f;                 // 紙の流れ・青の羽ペンの位置（小節単位）
    bool demoActive, playActive;
    int hudMeasureIndex = -1;
    bool finished, hasGamepad;
    float playerH = 0.5f;
    float playerVelocity, moveTarget = 0.5f, stickFiltered = 0.5f;
    float inputDir;              // スティックで動かそうとしている向き（-1〜1）
    bool magnetLocked;
    float stickDirSign;
    int magnetMissFrames;
    float magnetOffset, magnetOffsetVel;   // 吸い付いた瞬間の線とのずれ（なめらかに0へ縮める）
    float penScale = 1f;
    float deltaStick, deltaStickPrev;
    float rawStickSign = 1f;            // 生のスティック値の上下の向き（自動で判定）
    float freeTarget;                  // 吸着がなかった場合にスティックが向かわせたい高さ
    NoteView noteMagnetNote, noteMagnetEscaped;
    float noteMagnetBreakTimer;   // Deltaモード用：揺れを除いたスティック値
    float magnetMissTimer;
    bool holdActive, holdOnTrack;

    int perfect, good, miss, combo, maxCombo;
    string lastJudgeText = "", lastJudgeDetail = "";
    Color lastJudgeColor = Color.black;
    float lastJudgeTime = -10f;

    float BeatSec => 60f / bpm;
    float MeasureSec => BeatSec * beatsPerMeasure;
    float H(float h) => Mathf.Lerp(playYMin, playYMax, h);
    float BeatToX(float beat) => beat / beatsPerMeasure * measureLength;

    // ------------------------------------------------------------------ setup

    void Start()
    {
        SetupCamera();
        BuildStage();
        metronome = GetComponent<Metronome>();
        if (metronome == null) metronome = gameObject.AddComponent<Metronome>();
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.hideFlags = HideFlags.HideInInspector;

        string text = chartFile != null ? chartFile.text : DefaultChart;
        measures = Chart.Parse(text, beatsPerMeasure);
        Debug.Log($"[譜面] {measures.Count} 小節を読み込みました");

        StartSong();
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
        }
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = paperColor;
        cam.transform.position = new Vector3(0f, 0f, -10f);
    }

    void BuildStage()
    {
        var stage = new GameObject("Stage").transform;

        // 紙の上の線と、下の黒い地面
        Shapes.CreateSegment(stage, "PaperTop", new Vector3(-20f, paperTopY), new Vector3(20f, paperTopY), inkColor, 0.06f, 1);
        Shapes.CreateSegment(stage, "Floor", new Vector3(-20f, paperBottomY - 1.5f), new Vector3(20f, paperBottomY - 1.5f), inkColor, 3f, 1);

        // 黒い判定ラインと目盛り（高さ 0 / 0.5 / 1）
        Shapes.CreateSegment(stage, "JudgeLine", new Vector3(blackLineX, paperTopY), new Vector3(blackLineX, paperBottomY), inkColor, 0.14f, 3);
        foreach (float h in new[] { 0f, 0.5f, 1f })
            Shapes.CreateSegment(stage, "Tick", new Vector3(blackLineX - 0.28f, H(h)), new Vector3(blackLineX + 0.18f, H(h)), inkColor, 0.04f, 3);

        // 水色の小節線（青の羽ペンと一緒に動く）
        blueLine = new GameObject("BlueBarLine").transform;
        Shapes.CreateSegment(blueLine, "Line", new Vector3(0f, paperTopY), new Vector3(0f, paperBottomY), barLineColor, 0.07f, 2);

        // 羽ペン（仮：丸）
        bluePen = new GameObject("NarratorPen_Blue").transform;
        Shapes.CreateDisk(bluePen, "Disk", 0.22f, narratorColor, 11);

        yellowPen = new GameObject("PlayerPen_Yellow").transform;
        Shapes.CreateDisk(yellowPen, "Disk", 0.22f, playerColor, 12);

        notesRoot = new GameObject("Notes").transform;
    }

    void StartSong()
    {
        foreach (var g in groups) Destroy(g.root.gameObject);
        groups.Clear();
        nextGroupIndex = 0;
        guideKeys.Clear();
        paperScroll = penPaper = 0f;
        penH = 0.5f;
        demoActive = playActive = false;
        hudMeasureIndex = -1;
        BuildSchedule();
        finished = false;
        perfect = good = miss = combo = maxCombo = 0;
        lastJudgeText = lastJudgeDetail = "";
        noteMagnetNote = noteMagnetEscaped = null;
        magnetLocked = false;

        musicSource.Stop();
        if (music != null)
        {
            // 曲の「最初の1拍目」がゲームの0秒になるように、曲の再生を予約する
            songStartDsp = AudioSettings.dspTime + 0.6 + Math.Max(0.0, firstBeatTime);
            musicSource.clip = music;
            musicSource.volume = musicVolume;
            musicSource.PlayScheduled(songStartDsp - firstBeatTime);
            musicEndTime = music.length - firstBeatTime;
            if (metronomeWithMusic) metronome.Begin(songStartDsp, BeatSec, beatsPerMeasure, 0);
            else metronome.Stop();
        }
        else
        {
            // 1小節のカウントインのあとに始まる
            songStartDsp = AudioSettings.dspTime + 0.3 + MeasureSec;
            musicEndTime = double.MaxValue;
            metronome.Begin(songStartDsp, BeatSec, beatsPerMeasure, -beatsPerMeasure);
        }
        songTime = AudioSettings.dspTime - songStartDsp;
        prevSongTime = songTime;
    }

    // ------------------------------------------------------------------ loop

    void Update()
    {
        var gp = Gamepad.current;
        var kb = Keyboard.current;
        hasGamepad = gp != null;

        if ((gp != null && gp.startButton.wasPressedThisFrame) || (kb != null && kb.rKey.wasPressedThisFrame))
        {
            StartSong();
            return;
        }

        UpdateClock();
        double t = songTime;
        double dt = Math.Max(0.0, Math.Min(0.1, songTime - prevSongTime));
        prevSongTime = songTime;

        ReadInput(gp, kb, out bool pressed, out bool held);

        int loopIdx = t < 0 ? -1 : (int)Math.Floor(t / loopLen);
        bool ended = (!loop && loopIdx >= 1) || t > musicEndTime;
        if (ended && !finished) { finished = true; metronome.Stop(); }

        if (!ended)
        {
            // 新しい小節は、お手本が始まる時に作る
            while (t >= 0 && DemoStart(nextGroupIndex) <= t) CreateGroup(nextGroupIndex++);
            UpdateTimeline(t);
        }
        else
        {
            demoActive = playActive = false;
        }

        UpdateGroups();
        UpdateBluePen();

        double jt = t - inputOffsetMs / 1000.0;
        ApplyMagnet(jt, held);
        if (!magnetLocked) ApplyNoteMagnet(jt);
        else noteMagnetNote = null;
        if (pressed) OnPress(jt);
        UpdateHoldsAndMisses(jt, dt, held);
        UpdatePlayerPen(held);
    }

    void UpdateClock()
    {
        // オーディオの時計に合わせつつ、画面はなめらかに
        double target = AudioSettings.dspTime - songStartDsp;
        songTime += Time.unscaledDeltaTime;
        double diff = target - songTime;
        if (Math.Abs(diff) > 0.05) songTime = target;
        else songTime += diff * 0.1;
    }

    void ReadInput(Gamepad gp, Keyboard kb, out bool pressed, out bool held)
    {
        pressed = false;
        held = false;

        float stickY = 0f;
        if (gp != null)
        {
            // 生の値を読む（Input System標準の「中心デッドゾーン」は使わず、自前で処理する）
            // コントローラーによっては生の値の上下が逆になっている（Unityが補正前の値を返す）ので、
            // 補正済みの値と見比べて向きを自動で合わせる
            float rawY = gp.leftStick.ReadUnprocessedValue().y;
            float procY = gp.leftStick.ReadValue().y;
            if (Mathf.Abs(rawY) > 0.3f && Mathf.Abs(procY) > 0.3f)
                rawStickSign = Mathf.Sign(rawY) == Mathf.Sign(procY) ? 1f : -1f;
            stickY = Mathf.Clamp(rawY * rawStickSign, -1f, 1f);
            if (invertStick) stickY = -stickY;
            pressed |= gp.buttonSouth.wasPressedThisFrame;
            held |= gp.buttonSouth.isPressed;
            if (shoulderButtons)
            {
                // L / R（LB / RB）：交互に押すと「たかたか」が楽
                pressed |= gp.leftShoulder.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame;
                held |= gp.leftShoulder.isPressed || gp.rightShoulder.isPressed;
            }
            if (triggerButtons)
            {
                pressed |= gp.leftTrigger.wasPressedThisFrame || gp.rightTrigger.wasPressedThisFrame;
                held |= gp.leftTrigger.isPressed || gp.rightTrigger.isPressed;
            }
        }

        float kbDir = 0f;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) kbDir += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) kbDir -= 1f;
            pressed |= kb.spaceKey.wasPressedThisFrame;
            held |= kb.spaceKey.isPressed;
        }

        // 向きだけの入力（上=1／下=-1／ほぼ倒していない=0）。ロングノートの自動追従で使う
        {
            float vDir = kbDir != 0f ? kbDir : stickY;
            stickDirSign = Mathf.Abs(vDir) >= directionThreshold ? Mathf.Sign(vDir) : 0f;
        }

        float target;
        if (stickMode == StickMode.Velocity || gp == null)
        {
            // ── 速度モード：傾き＝動く向きと速さ。少し傾けるとゆっくり動き続ける ──
            //   傾き  0 ─ returnZone ─ stickDeadzone ─ 1
            //         真ん中へ戻る │ その場にとどまる │ 倒した向きへ動く（倒すほど速い）
            float v = kbDir != 0f ? kbDir : stickY;
            float raw = Mathf.Abs(v);

            if (raw < returnZone)
            {
                inputDir = 0f;
                if (returnToCenter && returnTime > 0f)
                    moveTarget = Mathf.Lerp(moveTarget, 0.5f, 1f - Mathf.Exp(-Time.deltaTime / returnTime));
            }
            else if (raw <= stickDeadzone)
            {
                inputDir = 0f;   // その場にとどまる
            }
            else
            {
                // 境目でカクッとしないように、閾値の外側を 0〜1 に引き直す
                float a = (raw - stickDeadzone) / (1f - stickDeadzone);
                inputDir = Mathf.Sign(v) * a;
                a = Mathf.Pow(a, speedCurve);   // 少し倒したときほど、よりゆっくり
                float speed = penSpeed;

                // 次のノートから遠く、そちらへ向かっているときは速くする
                double now = songTime - inputOffsetMs / 1000.0;
                NoteView next = FindNextNote(now);
                if (next != null)
                {
                    float dist = next.data.points[0].y - moveTarget;
                    float far = Mathf.Abs(dist) - noteMagnetRange * 0.5f;   // 吸着が届くところまでの残り
                    if (far > 0f && Mathf.Sign(dist) == Mathf.Sign(v))
                    {
                        speed *= Mathf.Lerp(1f, farSpeedBoost, Mathf.Clamp01(far / 0.4f));
                        float timeLeft = (float)(next.hitTime - now);
                        if (arriveInTime && timeLeft > 0.05f)
                            speed = Mathf.Max(speed, far / timeLeft * 1.3f);   // 倒しきれば間に合う速さ
                    }
                }
                moveTarget = Mathf.Clamp01(moveTarget + Mathf.Sign(v) * a * speed * Time.deltaTime);
            }
            target = moveTarget;
        }
        else if (stickMode == StickMode.Delta)
        {
            // ── 変化量モード：スティックを動かした分だけペンが動く（戻せば戻る） ──
            // 小さな揺れは無視し、それを超えた分だけ追いかける
            float d0 = stickY - deltaStick;
            if (Mathf.Abs(d0) > deltaJitter) deltaStick += d0 - Mathf.Sign(d0) * deltaJitter;
            if (Mathf.Abs(stickY) >= 0.999f || Mathf.Abs(stickY) <= 0.001f) deltaStick = stickY;

            float dy = deltaStick - deltaStickPrev;
            deltaStickPrev = deltaStick;
            moveTarget = Mathf.Clamp01(moveTarget + dy * deltaGain * 0.5f);
            target = moveTarget;
            inputDir = Mathf.Clamp(dy / Mathf.Max(Time.deltaTime, 1e-4f) * 0.25f, -1f, 1f);
        }
        else
        {
            // ── 位置モード：傾き＝高さ（ニュートラルで真ん中） ──
            float y = stickY;
            // 対数カーブ：y' = log(1 + k|y|) / log(1 + k)（k=0 でまっすぐ）
            if (stickLogCurve > 0.01f)
                y = Mathf.Sign(y) * Mathf.Log(1f + stickLogCurve * Mathf.Abs(y)) / Mathf.Log(1f + stickLogCurve);
            float h = Mathf.Clamp01((y + 1f) * 0.5f);

            // ぶるぶる除去：jitterFilter より小さい揺れは無視し、超えた分だけ追いかける
            float d = h - stickFiltered;
            if (Mathf.Abs(d) > jitterFilter) stickFiltered += d - Mathf.Sign(d) * jitterFilter;
            if (h <= 0.001f || h >= 0.999f) stickFiltered = h;
            target = stickFiltered;
            inputDir = Mathf.Clamp((target - playerH) * 10f, -1f, 1f);
            moveTarget = playerH;   // モードを切り替えたときに飛ばないように
        }

        freeTarget = target;

        // バネのように追いかける：動き出しと止まり際がなめらかになる
        if (stickSmoothing <= 0f)
        {
            playerH = target;
            playerVelocity = 0f;
        }
        else
        {
            playerH = Mathf.Clamp01(Mathf.SmoothDamp(playerH, target, ref playerVelocity, stickSmoothing,
                maxPenSpeed > 0f ? maxPenSpeed : Mathf.Infinity, Time.deltaTime));
        }
    }

    // ------------------------------------------------------------------ notes

    // ------------------------------------------------------------------ timeline

    /// <summary>
    /// 譜面1周ぶんの進行表を作る。
    ///   ふつうの小節： [お手本 1小節][プレイ 1小節]
    ///   flow の小節  ： 前の小節のプレイと同時にお手本、その直後にプレイ（止まらずに続く）
    /// </summary>
    void BuildSchedule()
    {
        int N = measures.Count;
        demoOff = new double[N];
        playOff = new double[N];
        double M = MeasureSec, cursor = 0;
        for (int m = 0; m < N; m++)
        {
            if (m > 0 && measures[m].flow)
            {
                demoOff[m] = playOff[m - 1];
                playOff[m] = playOff[m - 1] + M;
            }
            else
            {
                demoOff[m] = cursor;
                playOff[m] = cursor + M;
            }
            cursor = playOff[m] + M;
        }
        loopLen = cursor;

        // 次の小節にはみ出すノートがあるのに、次の小節が flow になっていないと時間がずれる
        for (int m = 0; m < N; m++)
        {
            float maxBeat = 0f;
            foreach (var n in measures[m].notes) maxBeat = Mathf.Max(maxBeat, n.EndBeat);
            int span = Mathf.Max(1, Mathf.CeilToInt(maxBeat / beatsPerMeasure - 1e-4f));
            for (int k = 1; k < span; k++)
                if (m + k >= N || !measures[m + k].flow)
                    Debug.LogWarning($"[譜面] {m + 1}小節目のノートが {m + k + 1}小節目まで続いています。{m + k + 1}小節目に flow を書いてください");
        }
    }

    double DemoStart(int i)
    {
        int N = measures.Count;
        return (i / N) * loopLen + demoOff[i % N];
    }

    double PlayStart(int i)
    {
        int N = measures.Count;
        return (i / N) * loopLen + playOff[i % N];
    }

    /// <summary>今の時間から、紙の流れ・青の羽ペンの位置・いまの状態を求める</summary>
    void UpdateTimeline(double t)
    {
        if (t < 0)
        {
            paperScroll = 0f;
            penPaper = 0f;
            demoActive = playActive = false;
            return;
        }

        int N = measures.Count;
        int loopIdx = (int)Math.Floor(t / loopLen);
        double local = t - loopIdx * loopLen;
        int baseI = loopIdx * N;
        double M = MeasureSec;

        // 紙の流れ：いちばん最近プレイが始まった小節
        paperScroll = baseI;
        playActive = false;
        int playIdx = -1;
        for (int m = 0; m < N; m++)
        {
            if (playOff[m] > local) break;
            double p = (local - playOff[m]) / M;
            paperScroll = baseI + m + (float)Math.Min(1.0, p);
            if (p < 1.0) { playActive = true; playIdx = baseI + m; }
        }

        // 青の羽ペン：お手本中の小節を書いている。お手本がなければ、書き終えた所にいる
        penPaper = baseI;
        demoActive = false;
        int demoIdx = -1;
        for (int m = 0; m < N; m++)
        {
            if (demoOff[m] > local) break;
            double p = (local - demoOff[m]) / M;
            if (p < 1.0) { penPaper = baseI + m + (float)p; demoActive = true; demoIdx = baseI + m; }
            else penPaper = baseI + m + 1;
        }

        hudMeasureIndex = playIdx >= 0 ? playIdx : demoIdx;
    }

    // ------------------------------------------------------------------ notes

    void CreateGroup(int i)
    {
        int N = measures.Count;
        var m = measures[i % N];
        var g = new MeasureGroup { index = i };
        g.root = new GameObject($"{i + 1}: 譜面 {i % N + 1}小節目{(m.flow ? "（flow）" : "")}").transform;
        g.root.SetParent(notesRoot, false);

        // 青の羽ペンの道筋に、この小節の点を足す（x＝通しの拍）
        float baseBeat = i * (float)beatsPerMeasure;
        float lastX = guideKeys.Count > 0 ? guideKeys[guideKeys.Count - 1].x : float.NegativeInfinity;
        bool startsAtZero = false;
        foreach (var d in m.notes) if (d.StartBeat < 0.001f) startsAtZero = true;
        if (lastX < baseBeat && !startsAtZero) guideKeys.Add(new Vector2(baseBeat, penH));
        foreach (var d in m.notes)
            foreach (var p in d.points) guideKeys.Add(new Vector2(baseBeat + p.x, p.y));
        guideKeys.Sort((a, b) => a.x.CompareTo(b.x));

        double playStart = PlayStart(i);
        foreach (var d in m.notes)
        {
            g.notes.Add(CreateNoteView(d, g, playStart));
            g.maxBeat = Mathf.Max(g.maxBeat, d.EndBeat);
        }
        groups.Add(g);
    }

    NoteView CreateNoteView(NoteData d, MeasureGroup g, double playStart)
    {
        var n = new NoteView { data = d };
        n.go = new GameObject(d.type == NoteType.Tap ? "Tap" : "Long");
        n.go.transform.SetParent(g.root, false);

        n.measureStart = playStart;   // このノートを「プレイする」小節の頭
        n.hitTime = n.measureStart + d.StartBeat * BeatSec;
        n.endTime = n.measureStart + d.EndBeat * BeatSec;

        if (d.type == NoteType.Tap)
        {
            n.go.transform.localPosition = new Vector3(BeatToX(d.StartBeat), H(d.points[0].y), 0f);
            n.outer = Shapes.CreateCircle(n.go.transform, "Ring", tapRadius, inkColor, 0.1f, 5);
        }
        else
        {
            int count = Mathf.Max(2, Mathf.CeilToInt((d.EndBeat - d.StartBeat) * 24f) + 1);
            n.sampleBeats = new float[count];
            n.samplePts = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float b = Mathf.Lerp(d.StartBeat, d.EndBeat, i / (count - 1f));
                n.sampleBeats[i] = b;
                n.samplePts[i] = new Vector3(BeatToX(b), H(d.HeightAt(b)), 0f);
            }
            // 黒い太線の上に白い細線を重ねて「縁取りの帯」に見せる
            n.outer = Shapes.CreateLine(n.go.transform, "Outer", inkColor, longWidth, 5);
            n.inner = Shapes.CreateLine(n.go.transform, "Inner", paperColor, longWidth - 0.16f, 6);
        }

        n.go.SetActive(false);   // 青の羽ペンが通ったら現れる
        return n;
    }

    void UpdateGroups()
    {
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var g = groups[i];
            float span = Mathf.Max(1f, Mathf.Ceil(g.maxBeat / beatsPerMeasure - 1e-4f));
            if (paperScroll - g.index > span + 0.35f)
            {
                Destroy(g.root.gameObject);
                groups.RemoveAt(i);
                continue;
            }
            // 紙の上の位置 − 流れた量 ＝ 画面の位置
            g.root.localPosition = new Vector3(blackLineX + (g.index - paperScroll) * measureLength, 0f, 0f);

            // 青の羽ペンが通った所まで描く
            float penBeat = (penPaper - g.index) * beatsPerMeasure;
            foreach (var n in g.notes)
            {
                if (n.fullyDrawn) continue;
                DrawPartial(n, penBeat);
            }
        }
    }

    void DrawPartial(NoteView n, float penBeat)
    {
        if (n.data.type == NoteType.Tap)
        {
            n.go.SetActive(penBeat >= n.data.StartBeat);
            return;
        }
        if (penBeat < n.data.StartBeat) { n.go.SetActive(false); return; }

        int k = 0;
        while (k < n.sampleBeats.Length && n.sampleBeats[k] <= penBeat) k++;
        if (k >= n.sampleBeats.Length) { DrawFull(n); return; }

        n.go.SetActive(true);
        buffer.Clear();
        for (int i = 0; i < k; i++) buffer.Add(n.samplePts[i]);
        buffer.Add(new Vector3(BeatToX(penBeat), H(n.data.HeightAt(penBeat)), 0f));
        SetLongPoints(n, buffer.ToArray());
    }

    void DrawFull(NoteView n)
    {
        n.go.SetActive(true);
        if (n.data.type == NoteType.Long) SetLongPoints(n, n.samplePts);
        n.fullyDrawn = true;
    }

    void SetLongPoints(NoteView n, Vector3[] pts)
    {
        n.outer.positionCount = pts.Length;
        n.outer.SetPositions(pts);
        n.inner.positionCount = pts.Length;
        n.inner.SetPositions(pts);
    }

    // ------------------------------------------------------------------ pens

    void UpdateBluePen()
    {
        // 書いている所（紙の上の位置）を画面に置く。flow 中は紙と一緒に進むので水色の線の位置に留まる
        if (guideKeys.Count > 0) penH = Chart.Interp(guideKeys, penPaper * beatsPerMeasure);
        float x = blackLineX + (penPaper - paperScroll) * measureLength;
        bluePen.position = new Vector3(x, H(penH), 0f);
        blueLine.position = new Vector3(x, 0f, 0f);
    }

    void UpdatePlayerPen(bool held)
    {
        yellowPen.position = new Vector3(blackLineX, H(playerH), 0f);
        float s = 1f;
        if (held) s = 1.25f;
        if (holdActive && holdOnTrack) s = 1.5f;
        if (noteMagnetNote != null) s = Mathf.Max(s, 1.15f);
        if (magnetLocked) s = 1.5f;
        penScale = Mathf.Lerp(penScale, s, 1f - Mathf.Exp(-Time.deltaTime / 0.06f));   // 大きさもなめらかに
        yellowPen.localScale = Vector3.one * penScale;
    }

    // ------------------------------------------------------------------ judge

    /// <summary>
    /// ロングノートの吸着：Aを押していて、スティックの向きが線の進む向きと合っていれば、線に吸い付いて追従する
    /// </summary>
    void ApplyMagnet(double t, bool held)
    {
        if (!longNoteMagnet || !held) { ReleaseMagnet(); return; }

        NoteView active = null;
        foreach (var g in groups)
            foreach (var n in g.notes)
                if (!n.judged && n.data.type == NoteType.Long && t >= n.hitTime && t <= n.endTime) { active = n; break; }
        if (active == null) { ReleaseMagnet(); return; }

        float beat = (float)((t - active.measureStart) / BeatSec);
        float curveH = active.data.HeightAt(beat);
        const float e = 0.06f;
        float slope = (active.data.HeightAt(beat + e) - active.data.HeightAt(beat - e)) / (2f * e);

        // 向きが「逆」かどうかだけを見る。傾きの大きさは関係なし。
        // 平らな所・スティックを倒していないときは「逆」とみなさない
        bool opposite;
        if (longAutoFollow)
        {
            // 自動追従：坂の所では「線が進む向き」にスティックを倒している間だけ追いかける
            // （傾きの大きさは関係なし。平らな所は倒さなくてOK）
            // 切り替えの先読み・後追い：少し先／少し前の向きに合っていてもOK
            float w = directionSwitchWindow;
            float slopeAhead = (active.data.HeightAt(beat + w + e) - active.data.HeightAt(beat + w - e)) / (2f * e);
            float slopeBehind = (active.data.HeightAt(beat - w + e) - active.data.HeightAt(beat - w - e)) / (2f * e);
            opposite = longRequireDirection
                       && !DirectionOk(slope) && !DirectionOk(slopeAhead) && !DirectionOk(slopeBehind);
        }
        else
        {
            opposite = Mathf.Abs(slope) >= flatSlope
                       && Mathf.Abs(inputDir) > 0.02f
                       && inputDir * Mathf.Sign(slope) < 0f;
        }

        if (!magnetLocked)
        {
            if (!opposite && (longAutoFollow || Mathf.Abs(playerH - curveH) <= magnetRange))
            {
                magnetLocked = true;
                magnetMissTimer = 0f;
                magnetOffset = playerH - curveH;   // ここから線へなめらかに寄せる
            }
        }
        else
        {
            if (longAutoFollow)
            {
                // 向きが合わないフレームが続いたら外れる（それまでは追従を続ける）
                magnetMissFrames = opposite ? magnetMissFrames + 1 : 0;
                if (magnetMissFrames > longGraceFrames) { ReleaseMagnet(); return; }
            }
            else
            {
                magnetMissTimer = opposite ? magnetMissTimer + Time.deltaTime : 0f;
                if (magnetMissTimer > magnetGrace) { ReleaseMagnet(); return; }
            }
        }

        if (magnetLocked)
        {
            // 線との差だけをなめらかに縮め、あとは線そのものに乗る（入力とケンカしないのでぶるぶるしない）
            magnetOffset *= magnetSnapTime <= 0f ? 0f : Mathf.Exp(-Time.deltaTime / magnetSnapTime);
            playerH = Mathf.Clamp01(curveH + magnetOffset);
            playerVelocity = 0f;
            moveTarget = playerH;      // 吸着が外れたとき、その場所から動き出す
            stickFiltered = playerH;
        }
    }

    /// <summary>
    /// ノートの高さへの吸着：次に来るノートの高さに近づくと、そこに吸い付いて止まる。
    /// スティックを強く倒すと抜けられる（抜けたノートには再び吸い付かない）。
    /// </summary>
    void ApplyNoteMagnet(double t)
    {
        if (!noteMagnet) { noteMagnetNote = null; return; }

        NoteView next = FindNextNote(t);
        if (next == null || next == noteMagnetEscaped) { noteMagnetNote = null; noteMagnetBreakTimer = 0f; return; }
        float nh = next.data.points[0].y;

        // 抜け出し：強く倒した（位置モードは、スティックが離れた高さを指した）
        if (noteMagnetNote == next)
        {
            bool pushing = stickMode == StickMode.Position
                ? Mathf.Abs(freeTarget - nh) > noteMagnetRange * 1.5f
                : Mathf.Abs(inputDir) >= noteMagnetBreak;
            noteMagnetBreakTimer = pushing ? noteMagnetBreakTimer + Time.deltaTime : 0f;
            if (noteMagnetBreakTimer > noteMagnetBreakTime)
            {
                noteMagnetBreakTimer = 0f;
                noteMagnetEscaped = next;
                noteMagnetNote = null;
                return;
            }
        }
        else
        {
            noteMagnetNote = Mathf.Abs(playerH - nh) <= noteMagnetRange ? next : null;
        }

        if (noteMagnetNote != null)
        {
            float k = noteMagnetSnapTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / noteMagnetSnapTime);
            playerH = Mathf.Lerp(playerH, nh, k);
            playerVelocity = 0f;
            moveTarget = playerH;
            stickFiltered = playerH;
        }
    }

    /// <summary>次に来る、すでに書かれているノート（ロングは始点。なぞり中のロングは除く）</summary>
    NoteView FindNextNote(double t)
    {
        NoteView next = null;
        foreach (var g in groups)
            foreach (var n in g.notes)
            {
                if (n.judged || !n.go.activeSelf) continue;
                if (n.data.type == NoteType.Long && t >= n.hitTime) continue;
                if (n.data.type == NoteType.Tap && t > n.hitTime + goodTime) continue;
                if (n.hitTime - t > noteMagnetLookahead) continue;
                if (next == null || n.hitTime < next.hitTime) next = n;
            }
        return next;
    }

    bool DirectionOk(float slope)
    {
        return Mathf.Abs(slope) < flatSlope || stickDirSign == Mathf.Sign(slope);
    }

    void ReleaseMagnet()
    {
        magnetMissFrames = 0;
        magnetLocked = false;
        magnetMissTimer = 0f;
    }

    void OnPress(double t)
    {
        NoteView best = null;
        double bestAbs = double.MaxValue;
        foreach (var g in groups)
            foreach (var n in g.notes)
            {
                if (n.judged || n.data.type != NoteType.Tap) continue;
                double a = Math.Abs(t - n.hitTime);
                if (a <= goodTime && a < bestAbs) { best = n; bestAbs = a; }
            }
        if (best == null) return;

        float dtSigned = (float)(t - best.hitTime);
        float dh = playerH - best.data.points[0].y;
        Judge j;
        if (Mathf.Abs(dtSigned) <= perfectTime && Mathf.Abs(dh) <= perfectHeight) j = Judge.Perfect;
        else if (Mathf.Abs(dh) <= goodHeight) j = Judge.Good;
        else j = Judge.Miss;

        string timing = dtSigned < -perfectTime ? "早い" : dtSigned > perfectTime ? "遅い" : "ぴったり";
        string height = dh > perfectHeight ? "高い" : dh < -perfectHeight ? "低い" : "ぴったり";
        Apply(best, j, $"タイミング {dtSigned * 1000f:+0;-0;0}ms（{timing}）／高さ {dh:+0.00;-0.00;0.00}（{height}）");
    }

    void UpdateHoldsAndMisses(double t, double dt, bool held)
    {
        holdActive = false;
        holdOnTrack = false;

        foreach (var g in groups)
            foreach (var n in g.notes)
            {
                if (n.judged) continue;

                if (n.data.type == NoteType.Tap)
                {
                    if (t > n.hitTime + goodTime) Apply(n, Judge.Miss, "見逃し");
                    continue;
                }

                if (t >= n.hitTime && t <= n.endTime)
                {
                    float beat = (float)((t - n.measureStart) / BeatSec);
                    float target = n.data.HeightAt(beat);
                    bool ok = held && Mathf.Abs(playerH - target) <= holdTolerance;
                    n.holdTotal += (float)dt;
                    if (ok) n.holdOk += (float)dt;
                    holdActive = true;
                    holdOnTrack |= ok;
                }
                else if (t > n.endTime)
                {
                    float ratio = n.holdTotal > 0f ? n.holdOk / n.holdTotal : 0f;
                    Judge j = ratio >= holdPerfectRatio ? Judge.Perfect : ratio >= holdGoodRatio ? Judge.Good : Judge.Miss;
                    Apply(n, j, $"なぞれた割合 {ratio * 100f:0}%");
                }
            }
    }

    void Apply(NoteView n, Judge j, string detail)
    {
        n.judged = true;
        Color c;
        switch (j)
        {
            case Judge.Perfect: perfect++; combo++; c = perfectColor; break;
            case Judge.Good: good++; combo++; c = goodColor; break;
            default: miss++; combo = 0; c = missColor; break;
        }
        maxCombo = Mathf.Max(maxCombo, combo);
        Shapes.SetColor(n.outer, c);   // 思い出せたノートは色づく

        lastJudgeText = j.ToString();
        lastJudgeDetail = detail;
        lastJudgeColor = j == Judge.Miss ? Color.gray : new Color(0.75f, 0.5f, 0.05f);
        lastJudgeTime = Time.time;
    }

    // ------------------------------------------------------------------ HUD

    void OnGUI()
    {
        float s = Screen.height / 1080f;
        var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(30 * s) };
        st.normal.textColor = Color.black;

        string phase = finished ? "おしまい"
            : songTime < 0 ? "カウント"
            : demoActive && playActive ? "いっしょに（お手本とあなたの番）"
            : demoActive ? "語り部のお手本"
            : playActive ? "あなたの番" : "";

        string dots = "";
        int beatIdx = (int)Math.Floor(songTime / BeatSec);
        int pos = ((beatIdx % beatsPerMeasure) + beatsPerMeasure) % beatsPerMeasure;
        for (int i = 0; i < beatsPerMeasure; i++) dots += i == pos ? "● " : "○ ";

        string measureInfo = hudMeasureIndex >= 0 && measures != null && !finished
            ? $"譜面 {hudMeasureIndex % measures.Count + 1}/{measures.Count} 小節目"
            : "";
        GUI.Label(new Rect(40 * s, 30 * s, 1000 * s, 50 * s), $"{phase}   {measureInfo}   {(finished ? "" : dots)}", st);

        var right = new GUIStyle(st) { alignment = TextAnchor.UpperRight, fontSize = Mathf.RoundToInt(26 * s) };
        GUI.Label(new Rect(Screen.width - 640 * s, 30 * s, 600 * s, 160 * s),
            $"Perfect {perfect}   Good {good}   Miss {miss}\nコンボ {combo}（最大 {maxCombo}）", right);

        if (Time.time - lastJudgeTime < 0.9f && lastJudgeText.Length > 0)
        {
            var big = new GUIStyle(st) { fontSize = Mathf.RoundToInt(64 * s), alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            big.normal.textColor = lastJudgeColor;
            GUI.Label(new Rect(0, 130 * s, Screen.width, 90 * s), lastJudgeText, big);
            var small = new GUIStyle(st) { fontSize = Mathf.RoundToInt(24 * s), alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(0, 215 * s, Screen.width, 40 * s), lastJudgeDetail, small);
        }

        if (finished)
        {
            int total = perfect + good + miss;
            float acc = total > 0 ? (perfect + good * 0.5f) / total * 100f : 0f;
            var big = new GUIStyle(st) { fontSize = Mathf.RoundToInt(48 * s), alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(0, 280 * s, Screen.width, 70 * s), $"思い出せた割合 {acc:0.0}%　（Start / R でもう一度）", big);
        }

        var hint = new GUIStyle(st) { fontSize = Mathf.RoundToInt(22 * s), alignment = TextAnchor.LowerCenter };
        hint.normal.textColor = Color.white;
        string controls = hasGamepad
            ? "左スティック：高さ　A / L / R：ノート（長いノートは押しっぱなし）　Start：リスタート"
            : "コントローラー未接続：W/S（↑/↓）で高さ　Space：ノート　R：リスタート";
        GUI.Label(new Rect(0, Screen.height - 70 * s, Screen.width, 50 * s), controls, hint);
    }

    // ------------------------------------------------------------------ sample

    const string DefaultChart = @"
# 1小節目：まずは同じ高さで3拍
tap 0 0.5
tap 1 0.5
tap 2 0.5
---
# 2小節目：だんだん上がる
tap 0 0.2
tap 1 0.5
tap 2 0.8
---
# 3小節目：スケッチのロングノート
long 0 0.8 | 1 0.75 | 2 0.3 | 2.75 0.15
---
# 4小節目：8分音符まじり
tap 0 0.3
tap 0.5 0.6
tap 1 0.3
tap 2 0.8
---
# 5小節目：タップ → 短いロング → タップ
tap 0 0.5
long 1 0.2 | 2 0.8
tap 2.5 0.5
---
# 6小節目：揺れるロング
long 0 0.5 | 0.75 0.85 | 1.5 0.2 | 2.25 0.85 | 3 0.5
";
}