using System.Collections.Generic;

[System.Serializable]
public class HorseSimData
{
    public long id;
    public string name; // 馬名
    public float pred; // 予測勝率(モデルの予測値)。この順位に終盤で近づけたい
    public string running_style; // 脚質: 逃げ/先行/差し/追込
    public float agari_speed;    // 上がり(終盤)の実際の速度(km/h)。データが無い場合は-1
    public float fade_rate;      // 終盤の失速率(正の値ほど失速する)
}

// 1レース分のデータ(race_idごとに複数の馬がhorsesに入る)
[System.Serializable]
public class RaceSimData
{
    public long race_id;
    public int course_len; // レース距離(m)
    public List<HorseSimData> horses;
}

[System.Serializable]
public class RaceSimDataList
{
    public List<RaceSimData> races;
}
