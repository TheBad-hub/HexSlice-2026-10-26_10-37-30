using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HexSlice.Gameplay;

namespace HexSlice.UI
{
    public class HUDController : MonoBehaviour
    {
        [Header("Источники событий (Ссылки на Gameplay)")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private SliceInputController sliceInput;

        [Header("Верхний HUD: Прогресс и Цель")]
        [SerializeField] private TMP_Text progressLabel;
        [SerializeField] private Image progressBarFill;
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private TMP_Text coinsLabel;

        [Header("Жизни (3 сердца/энергоячейки)")]
        [SerializeField] private Image[] lifeIcons;
        [SerializeField] private Color lifeActiveColor = new Color(0.95f, 0.25f, 0.35f, 1f);
        [SerializeField] private Color lifeLostColor = new Color(1f, 1f, 1f, 0.18f);

        [Header("Баннер статуса реза (Slice Status Banner)")]
        [SerializeField] private GameObject bannerRoot;
        [SerializeField] private CanvasGroup bannerCanvasGroup;
        [SerializeField] private Image bannerBackground;
        [SerializeField] private TMP_Text bannerMessageText;
        [SerializeField] private TMP_Text bannerTimerText;

        [Header("Цвета баннера реза")]
        [SerializeField] private Color bannerNeutralColor = new Color(0.12f, 0.16f, 0.25f, 0.92f);
        [SerializeField] private Color bannerCuttingColor = new Color(0.08f, 0.35f, 0.55f, 0.95f);
        [SerializeField] private Color bannerReadyColor = new Color(0.10f, 0.50f, 0.25f, 0.95f);
        [SerializeField] private Color bannerDangerColor = new Color(0.65f, 0.12f, 0.15f, 0.95f);

        [Header("Нижняя кнопка: Хроно-стазис")]
        [SerializeField] private Button stasisButton;
        [SerializeField] private Image stasisButtonBackground;
        [SerializeField] private Image stasisChargeRadialFill;
        [SerializeField] private TMP_Text stasisTitleText;
        [SerializeField] private TMP_Text stasisSubtitleText;

        [Header("Цвета кнопки стазиса")]
        [SerializeField] private Color stasisChargingColor = new Color(0.14f, 0.16f, 0.28f, 0.9f);
        [SerializeField] private Color stasisReadyColor = new Color(0.85f, 0.55f, 0.05f, 0.95f);
        [SerializeField] private Color stasisActiveColor = new Color(0.02f, 0.50f, 0.65f, 0.95f);

        [Header("Всплывающие уведомления (Toast)")]
        [SerializeField] private CanvasGroup toastCanvasGroup;
        [SerializeField] private TMP_Text toastMessageText;

        private Coroutine _toastCoroutine;

        private void Awake()
        {
            if (stasisButton != null)
            {
                stasisButton.onClick.AddListener(OnStasisButtonClicked);
            }

            if (bannerRoot != null)
                bannerRoot.SetActive(false);

            if (toastCanvasGroup != null)
                toastCanvasGroup.alpha = 0f;
        }

        private void OnEnable()
        {
            if (gameManager != null)
            {
                gameManager.OnLevelStarted += HandleLevelStarted;
                gameManager.OnProgressChanged += HandleProgressChanged;
                gameManager.OnLivesChanged += HandleLivesChanged;
                gameManager.OnCoinsChanged += HandleCoinsChanged;
                gameManager.OnStasisChanged += HandleStasisChanged;
                gameManager.OnToastMessage += ShowToast;
            }

            if (sliceInput != null)
            {
                sliceInput.OnBannerChanged += HandleBannerChanged;
            }
        }

        private void OnDisable()
        {
            if (gameManager != null)
            {
                gameManager.OnLevelStarted -= HandleLevelStarted;
                gameManager.OnProgressChanged -= HandleProgressChanged;
                gameManager.OnLivesChanged -= HandleLivesChanged;
                gameManager.OnCoinsChanged -= HandleCoinsChanged;
                gameManager.OnStasisChanged -= HandleStasisChanged;
                gameManager.OnToastMessage -= ShowToast;
            }

            if (sliceInput != null)
            {
                sliceInput.OnBannerChanged -= HandleBannerChanged;
            }
        }

        private void HandleLevelStarted(int levelIndex)
        {
            if (levelLabel != null)
                levelLabel.text = $"Lvl. {levelIndex}";
        }

        private void HandleProgressChanged(float currentShrink, float targetShrink)
        {
            if (progressLabel != null)
            {
                progressLabel.text = $"Cut off: <b>{currentShrink:F0}%</b> / {targetShrink:F0}%";
            }

            if (progressBarFill != null)
            {
                float fill01 = targetShrink > 0f ? Mathf.Clamp01(currentShrink / targetShrink) : 0f;
                progressBarFill.fillAmount = fill01;
            }
        }

        private void HandleLivesChanged(int currentLives, int maxLives)
        {
            if (lifeIcons == null) return;

            for (int i = 0; i < lifeIcons.Length; i++)
            {
                if (lifeIcons[i] == null) continue;
                bool isAlive = i < currentLives;
                lifeIcons[i].color = isAlive ? lifeActiveColor : lifeLostColor;
            }
        }

        private void HandleCoinsChanged(int totalCoins)
        {
            if (coinsLabel != null)
                coinsLabel.text = totalCoins.ToString();
        }

        private void HandleBannerChanged(SliceBannerData data)
        {
            if (bannerRoot == null) return;

            if (data.state == SliceBannerState.Hidden)
            {
                bannerRoot.SetActive(false);
                return;
            }

            bannerRoot.SetActive(true);

            if (bannerMessageText != null)
                bannerMessageText.text = data.message;

            if (bannerTimerText != null)
            {
                bannerTimerText.gameObject.SetActive(data.timeLeft > 0.05f);
                bannerTimerText.text = $"{data.timeLeft:F1}с";
            }

            if (bannerBackground != null)
            {
                switch (data.state)
                {
                    case SliceBannerState.Neutral:
                        bannerBackground.color = bannerNeutralColor;
                        break;
                    case SliceBannerState.Cutting:
                        bannerBackground.color = bannerCuttingColor;
                        break;
                    case SliceBannerState.Ready:
                        bannerBackground.color = bannerReadyColor;
                        break;
                    case SliceBannerState.Danger:
                        bannerBackground.color = bannerDangerColor;
                        break;
                }
            }
        }

        private void HandleStasisChanged(float charge01, bool isActive, float remainingSeconds)
        {
            if (stasisChargeRadialFill != null)
                stasisChargeRadialFill.fillAmount = charge01;

            if (stasisButton != null)
                stasisButton.interactable = !isActive && charge01 >= 1f;

            if (isActive)
            {
                if (stasisButtonBackground != null)
                    stasisButtonBackground.color = stasisActiveColor;

                if (stasisTitleText != null)
                    stasisTitleText.text = $"STASIS ACTIVE ({remainingSeconds:F1}с)";

                if (stasisSubtitleText != null)
                    stasisSubtitleText.text = "The droplets are slowed down";
            }
            else if (charge01 >= 1f)
            {
                if (stasisButtonBackground != null)
                    stasisButtonBackground.color = stasisReadyColor;

                if (stasisTitleText != null)
                    stasisTitleText.text = "ACTIVATE STASIS!";

                if (stasisSubtitleText != null)
                    stasisSubtitleText.text = "Press to slow down the field for 4.0s";
            }
            else
            {
                if (stasisButtonBackground != null)
                    stasisButtonBackground.color = stasisChargingColor;

                int pct = Mathf.RoundToInt(charge01 * 100f);
                if (stasisTitleText != null)
                    stasisTitleText.text = $"CHRONO-STASIS ({pct}%)";

                if (stasisSubtitleText != null)
                    stasisSubtitleText.text = "Cut close to the droplets to charge";
            }
        }

        private void OnStasisButtonClicked()
        {
            if (gameManager != null)
                gameManager.TryActivateStasis();
        }

        public void ShowToast(string message)
        {
            if (toastCanvasGroup == null || toastMessageText == null) return;

            toastMessageText.text = message;
            if (_toastCoroutine != null)
                StopCoroutine(_toastCoroutine);

            _toastCoroutine = StartCoroutine(ToastRoutine());
        }

        private IEnumerator ToastRoutine()
        {
            // Появление
            float t = 0f;
            while (t < 0.2f)
            {
                t += Time.deltaTime;
                toastCanvasGroup.alpha = Mathf.Lerp(0f, 1f, t / 0.2f);
                yield return null;
            }
            toastCanvasGroup.alpha = 1f;

            yield return new WaitForSeconds(1.8f);

            // Исчезновение
            t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                toastCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t / 0.35f);
                yield return null;
            }
            toastCanvasGroup.alpha = 0f;
        }
    }
}