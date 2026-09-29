using UnityEngine;
using System.Collections.Generic;



public class ChangeCamera : MonoBehaviour
{
    [Header("参照")]
    public RaceVisualizer racevisualizer;
    public List<Transform> targets = new List<Transform>(); //  ターゲットをリストに変更
    [Header("カメラ")]
    public Camera cam;

    [Header("ゾーン境界(先頭馬の進捗0~1)")]
    [Tooltip("この比率までは「スタート追従」(DynamicFollow)")]
    [Range(0f,1f)] public float startZoneEnd = 0.10f;
    [Tooltip("この比率までは固定カメラA")]
    [Range(0f,1f)] public float fixedZoneAEnd = 0.32f;
    [Tooltip("この比率までは固定カメラB")]
    [Range(0f,1f)] public float fixedZoneBEnd = 0.55f;
    [Tooltip("この比率までは固定カメラC")]
    [Range(0f,1f)] public float fixedZoneCEnd = 0.80f;

    [Header("固定カメラの位置(コース上の地点からオフセットで計算)")]
    public float fixedSideOffset = 24f; // 実際の競馬中継のパンカメラ(柵からおよそ20〜25m)を参考に70から縮小
    public float fixedHeightOffset = 3.2f;
    public float fixedBackOffset = 0f;
    public bool fixedOnRightSide = true;

    [Header("画角の調整")]
    public float baseFov = 18f; // 馬群がまとまっている時の画角
    public float maxFov = 45f;  // カメラを近づけた分、馬群が広がった時にフレームアウトしないよう上限を拡大
    public float fovSpreadFactor = 0.9f; // 広がり量に対するFOVの伸び方(近距離化に合わせて敏感に)
    public float fovSmoothSpeed = 2f;

    [Header("視線、追従の滑らかさ")]
    [Range(0f, 1f)] public float leaderFocusBias = 0.5f; // 0=中心点だけ見る 1=先頭馬だけ見る
    public float lookSmoothSpeed = 3f;
    public float positionSmoothSpeed = 0.125f;

    [Header("スタート")]
    public float startmMnDistance = 7.0f; // 最低限離す距離
    public float startZoomLimiter = 35.0f;  // 値が大きいほど、馬が広がった時にカメラが大きく引く
    public float startHeightOffset = 6.0f;  // カメラの高さ

    [Header("最終")]
    public float finishMinDistance = 4.0f; // 最低限離す距離
    public float finishZoomLimiter = 35.0f;  // 値が大きいほど、馬が広がった時にカメラが大きく引く
    public float finishHeightOffset = 1.5f;  // カメラの高さ



    void Start()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;

        transform.position = new Vector3(-6.9f, 4.5f, 0f);
    }
    void LateUpdate()
    {
        if (racevisualizer == null || targets == null || targets.Count == 0 ) return;

        float ratio = racevisualizer.LeaderRatio;

        if(ratio < startZoneEnd)
        {
            DynamicFollow(startmMnDistance,startZoomLimiter, startHeightOffset);
        }
        else if (ratio < fixedZoneAEnd)
        {
            float another = Mathf.Lerp(startZoneEnd, fixedZoneAEnd,0.5f);
            FixedPanAtRatio(another);
        }
        else if (ratio < fixedZoneBEnd)
        {
            float another = Mathf.Lerp(fixedZoneAEnd, fixedZoneBEnd, 0.5f);
            FixedPanAtRatio(another);
        }
        else if (ratio < fixedZoneCEnd)
        {
            float another = Mathf.Lerp(fixedZoneBEnd, fixedZoneCEnd, 0.5f);
            FixedPanAtRatio(another);
        }
        else
        {
            DynamicFollow(finishMinDistance, finishZoomLimiter, finishHeightOffset);
        }
    }



    // ========固定位置に据えたまま、馬群の方向へ滑らかにパンするカメラ ==========
    void FixedPanAtRatio(float anchorRatio)
    {
        if(!racevisualizer.EvaluateAtRatio(anchorRatio,out Vector3 trackPos, out Vector3 tangent, out Vector3 up))
        {return;}

        Vector3 right = Vector3.Cross(up,tangent).normalized;
        float side = fixedOnRightSide ? fixedSideOffset : -fixedSideOffset;

        Vector3 desirePosition = trackPos + right * side + up *fixedHeightOffset + tangent * fixedBackOffset;

        transform.position = desirePosition;
        LookAtPack();
        ApplyDynamicFov();
    }

    // ======== 馬群を追いかける移動カメラ =========
    void DynamicFollow(float minDist, float zoomLim, float heightOff)
    {
        Vector3 centerPoint = GetCenterPoint();
        float greatestDistance = GetGreatestDistance();
        float dynamicDistance = minDist + (greatestDistance / zoomLim * minDist);

        Vector3 averageForward = GetAverageForward();
        Vector3 desirePosition = centerPoint + (averageForward * dynamicDistance) + (Vector3.up * heightOff);

        transform.position = Vector3.Lerp(transform.position, desirePosition, positionSmoothSpeed);

        LookAtPack();
        ApplyDynamicFov();
    }

    //========= 共通:先頭馬+馬群中心をブレンドした注視点を滑らかに向く ========
    void LookAtPack()
    {
        //先頭だけ追うと後続がフレームアウトしやすく、中心点だけ追うと先頭が画面端に寄りやすいので、両方をブレンドして注視点にする
        Vector3 centerPoint = GetCenterPoint();
        Vector3 focusPoint = Vector3.Lerp(centerPoint, racevisualizer.currentPosition, leaderFocusBias);

        Vector3 direction = focusPoint - transform.position;
        if(direction.sqrMagnitude < 0.001f) return;

        Quaternion desireRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, desireRotation, Time.deltaTime * lookSmoothSpeed);
    }

    // ========== 共通：馬群の広がりに応じてFOVを調整し、画面外に馬が出るのを防ぐ ========
    void ApplyDynamicFov()
    {
        if(cam == null) return;

        float spread = GetGreatestDistance();
        float targetFov = Mathf.Clamp(baseFov + spread * fovSpreadFactor, baseFov, maxFov);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * fovSmoothSpeed);
    }

    // 馬の実際の位置(バウンド演出込み)ではなく、演出を含まない「素の」位置を使う。
    // これを使わずtransformを直接見ると、走行アニメーションの上下動・傾きにカメラまで一緒に揺れてしまう。
    Vector3 GetTargetPosition(int i)
    {
        if (racevisualizer != null && i < racevisualizer.logicalPositions.Count)
        {
            return racevisualizer.logicalPositions[i];
        }
        return targets[i].position;
    }

    // 全員の中心点を求める関数
    Vector3 GetCenterPoint()
    {
        var bounds = new Bounds(GetTargetPosition(0), Vector3.zero);
        for (int i = 1; i < targets.Count; i++)
        {
            bounds.Encapsulate(GetTargetPosition(i));
        }
        return bounds.center;
    }

    // 最も離れている馬同士の距離を求める関数
    float GetGreatestDistance()
    {
        var bounds = new Bounds(GetTargetPosition(0), Vector3.zero);
        for (int i = 1; i < targets.Count; i++)
        {
            bounds.Encapsulate(GetTargetPosition(i));
        }
        // X軸（前進方向）とZ軸（レーン方向）の広がりを考慮
        return Mathf.Max(bounds.size.x, bounds.size.z);
    }

    // 全員の平均的な前方向（向き）を取得する関数
    Vector3 GetAverageForward()
    {
        Vector3 forwardSum = Vector3.zero;
        if (racevisualizer != null && racevisualizer.logicalForwards.Count == targets.Count)
        {
            foreach (var f in racevisualizer.logicalForwards)
            {
                forwardSum += f;
            }
        }
        else
        {
            foreach (var t in targets)
            {
                forwardSum += Quaternion.Euler(0f, 90f, 0f) * t.forward;
            }
        }
        return forwardSum.normalized;
    }
}
