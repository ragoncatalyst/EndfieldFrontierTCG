using System.Collections;
using EndfieldFrontierTCG.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EndfieldFrontierTCG.CA
{
    public class CardView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        [Header("UI Refs")]
        public Image MainImage;
        public Image HPIcon;
        public TMP_Text HPText;
        public Image ATKIcon;
        public TMP_Text ATKText;
        public TMP_Text EffectInfoText;
        public TMP_Text NameText;

        [Header("Runtime State")] public int CA_ID;
        public int CA_HP;
        public int CA_ATK;
        public Vector3 CA_POS_INI;

        private RectTransform _rect;
        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private Coroutine _tiltRoutine;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
            _canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        public void Bind(CardData data)
        {
            CA_ID = data.CA_ID;
            NameText.text = data.CA_Name_DIS;
            EffectInfoText.text = data.CA_EffectInfo_DIS;
            CA_HP = data.CA_HPMaximum;
            CA_ATK = data.CA_ATK_INI;
            HPText.text = CA_HP.ToString();
            ATKText.text = CA_ATK.ToString();

            var sprite = Resources.Load<Sprite>("CA_MainImages/" + data.CA_MainImage);
            if (sprite != null)
            {
                MainImage.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"Main image not found for {data.CA_MainImage}");
            }

            CA_EffectInfo.BindEffectScripts(gameObject, CA_ID);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            CA_POS_INI = _rect.position;
            _canvasGroup.blocksRaycasts = false;
            _tiltRoutine = StartCoroutine(ca_Movement());
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_canvas == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas.transform as RectTransform, eventData.position, _canvas.worldCamera, out var localPoint);
            _rect.position = _canvas.transform.TransformPoint(localPoint);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _canvasGroup.blocksRaycasts = true;
            ca_MovementPaused();
            bool canPlace = ca_PlacementCheck();
            if (canPlace)
            {
                ca_DP();
            }
            else
            {
                StartCoroutine(ca_BackToHand());
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                CardInfoPanelController.Instance?.Show();
            }
        }

        private IEnumerator ca_Movement()
        {
            float t = 0f;
            while (true)
            {
                // Oscillate rotation around Z between -10 and +10 degrees over 1.2 seconds
                t += Time.deltaTime;
                float phase = (t % 1.2f) / 1.2f; // 0..1
                float angle = Mathf.Sin(phase * Mathf.PI * 2f) * 10f;
                _rect.rotation = Quaternion.Euler(0, 0, angle);
                yield return null;
            }
        }

        private void ca_MovementPaused()
        {
            if (_tiltRoutine != null)
            {
                StopCoroutine(_tiltRoutine);
                _tiltRoutine = null;
            }
            _rect.rotation = Quaternion.identity;
        }

        private bool ca_PlacementCheck()
        {
            // TODO: Replace with real placement and cost check logic based on CA_Type and player UI_Cost_DIS
            return false;
        }

        private void ca_DP()
        {
            // Deploy logic stub
        }

        private IEnumerator ca_BackToHand()
        {
            Vector3 start = _rect.position;
            Vector3 end = CA_POS_INI;
            float dur = 0.25f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / dur);
                _rect.position = Vector3.Lerp(start, end, p);
                yield return null;
            }
            _rect.position = end;
        }
    }
}


