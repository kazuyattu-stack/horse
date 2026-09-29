using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections;

public class RaceVisualizer : MonoBehaviour
{

    [SerializeField] private SplineContainer spline;
    public GameObject horsePrefab; // ステップで作った青いCubeプレハブ
    public int raceDistance = 2000; // レースのゴール距離(targetRaceIdに対応するcourse_lenから自動決定される)

    [Header("シミュレートしたいレースのIDを入力")]
    public string targetRaceId = "202301010102"; // 入力したレースを走らせます！

    private List<HorseSimData> runners = new List<HorseSimData>();
    public List<GameObject> horseObjects = new List<GameObject>();
    public Dictionary<long, float> distanceTracker = new Dictionary<long, float>();
    private Dictionary<long, float> baseSpeedById = new Dictionary<long, float>(); // 各馬の巡航速度(km/h、上がり速度を基準にランダム決定)
    private Dictionary<long, int> predRankById = new Dictionary<long, int>(); // predが高い順の絶対順位(0=本命)
    private List<float> lateralOffsets = new List<float>(); // 各馬の柵からの横方向オフセット(m、0=柵ぎりぎり内側)
    private bool isRacing = false;

    // カメラ用: バウンド/ピッチ/傾きの演出を含まない「素の」位置・向き(カメラが一緒に揺れないようにするため)
    public List<Vector3> logicalPositions = new List<Vector3>();
    public List<Vector3> logicalForwards = new List<Vector3>();

    public  float currentProgress = 0f;

    public Vector3 currentPosition = Vector3.zero;

    //先頭馬がコース全長のうち何割まで進んだか(0~1)。カメラ側のゾーン判定などに使う。
    public float LeaderRatio  { get ; private set ;}

    [Header("自然な走りの調整")]
    [SerializeField] private float laneWidth = 1.5f; // スタート直後の初期横間隔
    [SerializeField] private float railSide = 1f; // 内側(柵)方向の符号。並ぶ向きが逆に見えたら -1 にする
    [SerializeField] private float railAttraction = 2f; // 内側(柵)へ寄る/追い越しで外に避ける速さ(m/s)。詰まった時にすぐ避けられるよう十分速くする
    [SerializeField] private float minLaneSeparation = 1.3f; // 近くにいる馬と保つ最低の横間隔(m)
    [SerializeField] private float lateralProximityWindow = 10f; // この前後距離(m)以内にいる馬とだけ間隔を調整する(詰まる前に避け始めるための余裕)
    [SerializeField] private float followGap = 2f; // 前の馬との距離がこれ未満だと減速し始める(m)
    [SerializeField] private float minFollowSpeedFactor = 0.85f; // 詰まって避けきれない時の最低速度倍率(押し抜けて見えないよう、はっきり減速する)
    [SerializeField] private float finalStretchLength = 400f; // ゴール手前この距離からスパートする(m)
    [SerializeField] private float finalStretchBoostFactor = 0.15f; // 上がり速度データが無い馬向けの、スパート時の最大速度上乗せ率
    [SerializeField] private float bobAmplitude = 0.05f; // 走行時の上下バウンドの大きさ
    [SerializeField] private float pitchAmplitude = 3f; // 走行時の前後ピッチ角(度)

    [Header("脚質ごとのペース配分(horse_simulation_data.json)")]
    [SerializeField] private float nigeEarlyPace = 1.05f; // 逃げ: 序盤から飛ばす
    [SerializeField] private float senkouEarlyPace = 1.02f; // 先行: やや前で構える
    [SerializeField] private float sashiEarlyPace = 0.96f; // 差し: 序盤は抑える
    [SerializeField] private float oikomiEarlyPace = 0.90f; // 追込: 序盤はかなり抑える
    [SerializeField] private float earlyPaceDecayDistance = 600f; // この距離を過ぎると脚質による序盤ペース差は薄れ、集団のペースに戻る(m)
    [SerializeField] private float fadeImpact = 0.3f; // fade_rateが終盤の速度に与える最大影響度
    [SerializeField] private float predRankCorrectionStrength = 0.02f; // 予測順位との1ランク差につき、終盤で速度をこの割合だけ補正する

    [Header("巡航速度のランダム決定(上がり速度を基準にする)")]
    [SerializeField] private float defaultBaseSpeedKmh = 60f; // 上がり速度データが無い場合の基準速度(km/h)
    [SerializeField] private float baseSpeedRandomMin = 0.97f; // 基準速度に対するランダム倍率(下限)
    [SerializeField] private float baseSpeedRandomMax = 1.03f; // 基準速度に対するランダム倍率(上限)

    private GameObject people;
    private GameObject trumpet;
    private GameObject pistle;
    private AudioSource audioSource = null;


    void Start()
    {
        people = GameObject.Find("People");
        trumpet = GameObject.Find("Trumpet");
        pistle = GameObject.Find("Pistle");
        audioSource = GetComponent<AudioSource>();

        // horse_simulation_data.json(脚質・上がり速度・馬名などの詳細データ)から出走馬を生成する
        string simFilePath = Path.Combine(Application.dataPath, "horse_simulation_data.json");
        if (!File.Exists(simFilePath))
        {
            Debug.LogError($"JSONファイルが見つかりません: {simFilePath}");
            return;
        }

        string simJsonText = File.ReadAllText(simFilePath);
        // JSONの配列形式をパースするために少し工夫(レース単位のオブジェクトが並んでいる配列)
        string wrappedSimJson = "{\"races\":" + simJsonText + "}";
        RaceSimDataList simDataList = JsonUtility.FromJson<RaceSimDataList>(wrappedSimJson);

        // 💡 targetRaceId と一致するレースを探す
        RaceSimData targetRace = null;
        foreach (RaceSimData race in simDataList.races)
        {
            if (race.race_id.ToString() == targetRaceId)
            {
                targetRace = race;
                break;
            }
        }

        if (targetRace == null || targetRace.horses == null || targetRace.horses.Count == 0)
        {
            Debug.LogError($"指定された race_id [{targetRaceId}] の出走馬データが見つかりません。");
            return;
        }

        List<HorseSimData> filteredRunners = targetRace.horses;

        // 💡 targetRaceId に対応するcourse_len(レース距離)から自動的に決定する
        raceDistance = targetRace.course_len;
        Debug.Log($"race_id [{targetRaceId}] のレース距離を自動決定しました: {raceDistance}m");

        // 💡 スプラインの実際のスタート地点(進捗0)を基準にスポーンさせる(ワールド原点は無関係)
        EvaluateAtRatio(0f, out Vector3 startPos, out Vector3 startTangent, out Vector3 startUp);
        Vector3 startRight = Vector3.Cross(startUp, startTangent).normalized * railSide;
        Quaternion startRotation = Quaternion.LookRotation(startTangent, startUp) * Quaternion.Euler(0f, 180f, 0f);

        // フィルタリングされたデータのみで馬を生成
        for (int i = 0; i < filteredRunners.Count; i++)
        {
            HorseSimData sim = filteredRunners[i];

            // 💡 【重複ガード】すでに同じIDの馬が追加されている場合は、エラーを防ぐためにスキップする
            if (distanceTracker.ContainsKey(sim.id))
            {
                continue;
            }

            runners.Add(sim);

            // 巡航速度のデータは無いため、実際の上がり速度を基準にランダムで決める
            float agariBase = sim.agari_speed > 0f ? sim.agari_speed : defaultBaseSpeedKmh;
            float baseSpeedKmh = agariBase * UnityEngine.Random.Range(baseSpeedRandomMin, baseSpeedRandomMax);
            baseSpeedById.Add(sim.id, baseSpeedKmh);

            // 生成される馬の数（インデックス）に合わせて綺麗にレーンを分ける
            int laneIndex = horseObjects.Count;
            float laneOffset = laneIndex * laneWidth;
            Vector3 spawnPos = startPos + startRight * laneOffset;
            GameObject horse = Instantiate(horsePrefab, spawnPos, startRotation);
            horseObjects.Add(horse);
            lateralOffsets.Add(laneOffset); // スタート時は外側から順に並べ、レース中に内側へ寄っていく
            logicalPositions.Add(spawnPos);
            logicalForwards.Add(-startTangent);

            horse.name = string.IsNullOrEmpty(sim.name) ? sim.id.ToString() : sim.name;
            distanceTracker.Add(sim.id, 0f); // これで重複エラーが絶対に起きなくなります
        }

        // predEnter(予測勝率)が高い順に絶対順位を割り当てておく(0=本命)。終盤でこの順位に近づける
        List<HorseSimData> byPred = new List<HorseSimData>(runners);
        byPred.Sort((a, b) => b.pred.CompareTo(a.pred));
        for (int rank = 0; rank < byPred.Count; rank++)
        {
            predRankById[byPred[rank].id] = rank;
        }

        if (horseObjects.Count > 0)
        {
            ChangeCamera cameraScript = Camera.main.GetComponent<ChangeCamera>();

            if (cameraScript != null)
            {
                // カメラの追従リストを一度クリアし、今回走るすべての馬をセット
                cameraScript.targets.Clear();
                foreach (GameObject horseObj in horseObjects)
                {
                    cameraScript.targets.Add(horseObj.transform);
                }

                Debug.Log($"【成功】すべての馬（{horseObjects.Count}頭）をカメラの追従対象に設定しました！");
            }
            else
            {
                Debug.LogWarning("Main Cameraに 'FrontCameraFollow' スクリプトがアタッチされていません。");
            }
        }
        StartCoroutine(StartCountdown());
        Debug.Log($"レース開始！ race_id: {targetRaceId} (出走馬：{runners.Count}頭)");
    }

    // 脚質(running_style)ごとの序盤ペース倍率を返す
    private float GetEarlyPaceMultiplier(string runningStyle)
    {
        switch (runningStyle)
        {
            case "逃げ": return nigeEarlyPace;
            case "先行": return senkouEarlyPace;
            case "差し": return sashiEarlyPace;
            case "追込": return oikomiEarlyPace;
            default: return 1f;
        }
    }

    IEnumerator StartCountdown()
    {
        Debug.Log("準備中...");
        yield return new WaitForSeconds(8f); // ここで8秒待つ（Updateを止めない）
        audioSource = trumpet.GetComponent<AudioSource>();
        audioSource.Stop();
        yield return new WaitForSeconds(2f); // ここで2秒待つ（Updateを止めない）
        audioSource = pistle.GetComponent<AudioSource>();
        audioSource.Play();
        yield return new WaitForSeconds(1f); // ここで1秒待つ（Updateを止めない）
        audioSource = people.GetComponent<AudioSource>();
        audioSource.Play();

        isRacing = true;
        Debug.Log("スタート！");
    }

    //指定した進捗比率(0=スタート地点、　1=ゴール)のコース上の位置・接線・法線を取得する
    //ChangeCamera側が「コースのどのあたりにカメラを据えるか」を、決め打ちの座標ではなく、コースの実際の形状から計算するために使う。
    public bool EvaluateAtRatio(float ratio, out Vector3 position, out Vector3 tangent, out Vector3 up)
    {
        position = Vector3.zero;
        tangent = Vector3.forward;
        up = Vector3.up;

        if(!spline) return false;

        float length = spline.CalculateLength();
        if (length <= 0f) return false;

        ratio = Mathf.Clamp01(ratio);
        //Update()内の馬の移動と同じ向き(1-t評価)で合わせる
        spline.Evaluate(1f - ratio, out float3 p, out float3 tan, out float3 u);

        position = p;
        tangent = tan;
        up = u;
        return true;
    }

    void Update()
    {
        // 状態によって処理を分ける
        if (!isRacing)
        {
            // 待機中は何もさせない（または演出のみ）
            return;
        }


        //Splineや追従オブジェクトの失効などを検知してエラー防止
        if (!spline || horseObjects.Count==0) return;
        if (spline.CalculateLength() == 0f) return;

        float topDistance = 0f;

        for (int i = 0; i < runners.Count; i++)
        {
            HorseSimData runner = runners[i];
            float currentProgress = distanceTracker[runner.id]; // 現在何メートル走ったか

            if (currentProgress < raceDistance)
            {
                // 💡 1. 巡航速度(上がり速度を基準にランダムで決めた値)を基準にする
                float targetSpeedKmh = baseSpeedById[runner.id];

                // 💡 2b. 真正面(同じレーン付近)に馬がいる場合だけ、詰まって減速する
                float aheadFactor = 1f;
                for (int j = 0; j < runners.Count; j++)
                {
                    if (j == i) continue;
                    float gap = distanceTracker[runners[j].id] - currentProgress;
                    float lateralGap = Mathf.Abs(lateralOffsets[j] - lateralOffsets[i]);

                    if (gap > 0f && gap < followGap && lateralGap < minLaneSeparation)
                    {
                        aheadFactor = Mathf.Min(aheadFactor, Mathf.Clamp01(gap / followGap));
                    }
                }
                targetSpeedKmh *= Mathf.Lerp(minFollowSpeedFactor, 1f, aheadFactor);

                // 💡 2c. 脚質(逃げ/先行/差し/追込)に応じて序盤のペースを変え、終盤は実際の上がり速度データに寄せる
                // 序盤ペース差は距離が進むにつれて薄れ、集団のペースに収束していく(ずっと8%速いままだと数分のレースで独走になってしまうため)
                float earlyPaceT = Mathf.Clamp01(1f - currentProgress / earlyPaceDecayDistance);
                float earlyPaceMultiplier = Mathf.Lerp(1f, GetEarlyPaceMultiplier(runner.running_style), earlyPaceT);

                float distanceToFinish = raceDistance - currentProgress;
                if (distanceToFinish < finalStretchLength && distanceToFinish >= 0f)
                {
                    float stretchT = 1f - (distanceToFinish / finalStretchLength);

                    if (runner.agari_speed > 0f)
                    {
                        // 実データの上がり(終盤)速度を目標に、序盤ペースからスムーズに切り替える
                        float fadeMultiplier = 1f - Mathf.Clamp01(runner.fade_rate) * fadeImpact;
                        float targetAgariSpeed = runner.agari_speed * fadeMultiplier;
                        targetSpeedKmh = Mathf.Lerp(targetSpeedKmh * earlyPaceMultiplier, targetAgariSpeed, stretchT);
                    }
                    else
                    {
                        // 上がり速度データが無い馬は、従来通り一律のスパート倍率を使う
                        targetSpeedKmh *= earlyPaceMultiplier;
                        targetSpeedKmh *= 1f + stretchT * finalStretchBoostFactor;
                    }
                }
                else
                {
                    targetSpeedKmh *= earlyPaceMultiplier;
                }

                // 💡 2d. predによる予測順位に、終盤で少しずつ帳尻を合わせる
                if (distanceToFinish < finalStretchLength && distanceToFinish >= 0f
                    && predRankById.TryGetValue(runner.id, out int predRank))
                {
                    int currentRank = 0;
                    for (int j = 0; j < runners.Count; j++)
                    {
                        if (j == i) continue;
                        if (distanceTracker[runners[j].id] > currentProgress) currentRank++;
                    }

                    // 正:予測より下位を走っている(加速させたい)、負:予測より上位を走っている(抑えたい)
                    float rankError = currentRank - predRank;
                    float stretchT2 = 1f - (distanceToFinish / finalStretchLength);
                    targetSpeedKmh *= 1f + rankError * predRankCorrectionStrength * stretchT2;
                }

                // 💡 3. 【最重要】時速 (km/h) を、Unityの移動で使う秒速 (m/s) に変換！
                float speedMs = targetSpeedKmh / 3.6f;

                // 💡 4. 移動距離を計算してトラッカーに反映
                float moveDistance = speedMs * Time.deltaTime;
                distanceTracker[runner.id] += moveDistance;

                // レース距離全体を1周分の進捗として扱う(トラックの見た目スケールに依存しない)
                float progressRatio = math.saturate(distanceTracker[runner.id] / raceDistance);

                // Splineの計算をする核心部分
                spline.Evaluate(1f - progressRatio, out float3 pos, out float3 tangent, out float3 up);

                // 一部のノット(スタート/ゴール付近)は接線ハンドルが極端に短く、解析的な接線がほぼ0ベクトルになって
                // 回転が不安定になることがあるため、その場合は少し先の実座標との差分から向きを推定して補う
                if (math.lengthsq(tangent) < 0.0001f)
                {
                    float sCurrent = 1f - progressRatio;
                    float sNear = (sCurrent + 0.002f) % 1f;
                    spline.Evaluate(sNear, out float3 posNear, out _, out _);
                    Vector3 fallbackTangent = (Vector3)posNear - (Vector3)pos;
                    if (fallbackTangent.sqrMagnitude > 0.0001f)
                    {
                        tangent = fallbackTangent;
                    }
                }

                if (math.any(tangent))
                {
                    // インコース(柵側)を積極的に取り合うよう、近くの馬との間隔を保ちつつ内側へ寄っていく
                    float minAllowed = 0f; // 柵より内側には出られない
                    float maxAllowed = float.PositiveInfinity;
                    for (int j = 0; j < runners.Count; j++)
                    {
                        if (j == i) continue;
                        float progressGap = Mathf.Abs(distanceTracker[runners[j].id] - currentProgress);
                        if (progressGap < lateralProximityWindow)
                        {
                            float otherOffset = lateralOffsets[j];
                            if (otherOffset <= lateralOffsets[i])
                            {
                                minAllowed = Mathf.Max(minAllowed, otherOffset + minLaneSeparation);
                            }
                            else
                            {
                                maxAllowed = Mathf.Min(maxAllowed, otherOffset - minLaneSeparation);
                            }
                        }
                    }
                    float desiredOffset = Mathf.Clamp(0f, minAllowed, Mathf.Max(minAllowed, maxAllowed));
                    lateralOffsets[i] = Mathf.MoveTowards(lateralOffsets[i], desiredOffset, railAttraction * Time.deltaTime);

                    Vector3 right = Vector3.Cross((Vector3)up, (Vector3)tangent).normalized * railSide;
                    Vector3 finalPos = (Vector3)pos + right * lateralOffsets[i];

                    // カメラ用: バウンド演出を足す前の「素の」位置・向きを記録しておく
                    // tangentはスプライン評価の都合で進行方向と逆を向いているため、反転して実際の進行方向にする
                    logicalPositions[i] = finalPos;
                    logicalForwards[i] = -(Vector3)tangent;

                    // 走行アニメーション用の上下バウンドと前後ピッチ(馬ごとに位相をずらす)
                    float strideHz = Mathf.Lerp(1.5f, 4f, Mathf.InverseLerp(0f, 20f, speedMs));
                    float phase = Time.time * strideHz * Mathf.PI * 2f + i * 0.9f;
                    float bob = Mathf.Sin(phase) * bobAmplitude;
                    float pitch = Mathf.Sin(phase * 2f) * pitchAmplitude;
                    finalPos += (Vector3)up * bob;

                    horseObjects[i].transform.position = finalPos;
                    horseObjects[i].transform.rotation = Quaternion.LookRotation((Vector3)tangent, (Vector3)up)*Quaternion.Euler(pitch ,180f, 0f);
                }
            }

            if (distanceTracker[runner.id] > topDistance)
            {
                topDistance = distanceTracker[runner.id];
                currentPosition = logicalPositions[i];
            }
        }
        this.currentProgress = topDistance;
        LeaderRatio = math.saturate(topDistance / raceDistance);
    }
}
