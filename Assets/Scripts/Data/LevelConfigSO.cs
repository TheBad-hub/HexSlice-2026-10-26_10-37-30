using System;
using UnityEngine;

namespace HexSlice.Data
{
    [Serializable]
    public struct BubbleSpawnEntry
    {
        public BubbleConfigSO bubbleConfig;
        [Min(1)] public int count;
    }

    [CreateAssetMenu(fileName = "Level_01", menuName = "HexSlice/Level Config")]
    public class LevelConfigSO : ScriptableObject
    {
        [Header("Основные параметры уровня")]
        [Min(1)] public int levelIndex = 1;
        [Range(50f, 90f)] public float targetShrinkPercent = 75f;
        [Min(1)] public int maxLives = 3;
        [Min(1.5f)] public float hexRadius = 3.2f;

        [Header("Визуальная тема соты")]
        [Tooltip("Материал меша соты с тайловой текстурой гексагональной сетки")]
        public Material boardFillMaterial;
        [Tooltip("Масштаб текстуры сот внутри многоугольника (UV Tiling)")]
        public float uvTiling = 0.25f;
        [ColorUsage(true, true)] public Color activeBorderColor = new Color(0f, 0.94f, 1f, 1f);
        public Color ghostBorderColor = new Color(1f, 1f, 1f, 0.15f);

        [Header("Спавн капелек (Шариков)")]
        public BubbleSpawnEntry[] bubbles;

        [Header("Greed-эффект и Награды")]
        [Range(0, 6)] public int greedCoinsCount = 2;
        [Min(1)] public int coinValue = 5;
        [Min(0)] public int levelClearCoinReward = 10;
        public bool grantsRelicShard = true;
    }
}