using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EndfieldFrontierTCG.UI
{
    public class CardInfoPanelController : MonoBehaviour
    {
        public static CardInfoPanelController Instance { get; private set; }

        [Header("Panel Root")]
        public GameObject PanelRoot;

        [Header("UI Refs")]
        public Image MainImage;
        public TMP_Text HPText;
        public TMP_Text HPMaxText;
        public TMP_Text ATKText;
        public TMP_Text ATKIniText;
        public TMP_Text EffectInfoText;
        public TMP_Text NameText;
        public RawImage MainImageRaw; // optional: for 3D tex

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Hide();
        }

        // Note: ShowFromCard overloads referencing legacy CardView / CardView3D were removed
        // because the runtime card implementation has been rewritten. Use Show() / Hide()
        // directly, or add a new data-driven method if needed.

        public void Show()
        {
            if (PanelRoot != null) PanelRoot.SetActive(true);
        }

        public void Hide()
        {
            if (PanelRoot != null) PanelRoot.SetActive(false);
        }
    }
}


