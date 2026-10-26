using UnityEngine;

namespace HexSlice.Data
{
    public enum BubbleBehaviorType
    {
        Standard,       // Уровни 1–20: равномерное движение
        HeavySlow,      // Уровни 20+: крупный, медленный, перекрывает широкие сектора
        ImpulseDash,    // Уровни 20+: двигается рывками (разгон -> скольжение)
        WallBoost       // Уровни 20+: временно ускоряется после каждого отскока от стены
    }

    [CreateAssetMenu(fileName = "BubbleConfig_", menuName = "HexSlice/Bubble Config")]
    public class BubbleConfigSO : ScriptableObject
    {
        [Header("Визуал и Текстуры (Скин)")]
        public string skinId = "default";
        public Sprite bodySprite;
        public Sprite glossSprite;
        public Sprite shadowSprite;
        public Sprite eyeWhiteSprite;
        public Sprite pupilSprite;
        public Sprite sweatDropSprite;
        public Color bodyTint = Color.white;

        [Header("Физика и Размеры")]
        [Min(0.1f)] public float radius = 0.42f;
        [Min(0.5f)] public float baseSpeed = 3.2f;
        public BubbleBehaviorType behavior = BubbleBehaviorType.Standard;

        [Header("Параметры спец-поведения (Уровни 20+)")]
        [Tooltip("Для ImpulseDash: частота рывков в секундах")]
        public float dashPeriod = 1.6f;
        [Tooltip("Для WallBoost: множитель скорости сразу после удара о стену")]
        public float wallBoostMultiplier = 1.65f;

        [Header("Деформация (Squash & Stretch)")]
        [Range(0.4f, 0.9f)] public float impactSquash = 0.65f;
        public float squashRecoverySpeed = 9f;
        public float idleWobbleSpeed = 6f;
        public float idleWobbleAmount = 0.045f;
    }
}