using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EndfieldFrontierTCG.CA;
using EndfieldFrontierTCG.Hand;
using EndfieldFrontierTCG.Deck;
using EndfieldFrontierTCG.Board;

namespace EndfieldFrontierTCG.Deck
{
    public class DeckDrawController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CA_TypeManager typeManager;
        [SerializeField] private HandSplineZone handZone;
        [SerializeField] private Transform deckSpawnPoint;
        [SerializeField] private Transform deckInitialOrientation;
        [SerializeField] private EventPlayZone overflowEventZone;
        [SerializeField] private int maxHandCards = 10;

        [Header("Deck Definition")]
        [SerializeField] private List<DeckEntry> starterDeck = new List<DeckEntry>();
        [SerializeField] private int shuffleSeed = 12345;

        private Queue<int> _deckQueue;

        private void Awake()
        {
            BuildDeckQueue();
        }

        private void BuildDeckQueue()
        {
            CardDatabase.EnsureLoaded();

            if (starterDeck == null || starterDeck.Count == 0)
            {
                Debug.LogWarning("DeckDrawController: starterDeck is empty.");
                _deckQueue = new Queue<int>();
                return;
            }

            var deckList = new DeckList
            {
                Entries = starterDeck
                    .Where(entry => entry != null && entry.Count > 0)
                    .Select(entry => new DeckEntry { CA_ID = entry.CA_ID, Count = entry.Count })
                    .ToList()
            };

            if (deckList.Entries.Count == 0)
            {
                _deckQueue = new Queue<int>();
                return;
            }

            var order = DeckShuffler.ShuffleWithBuckets(deckList, shuffleSeed);
            _deckQueue = new Queue<int>(order);
        }

        public void ShuffleRemaining()
        {
            if (_deckQueue == null || _deckQueue.Count <= 1) return;

            var deckList = new DeckList
            {
                Entries = _deckQueue
                    .GroupBy(id => id)
                    .Select(g => new DeckEntry { CA_ID = g.Key, Count = g.Count() })
                    .ToList()
            };

            var order = DeckShuffler.ShuffleWithBuckets(deckList, shuffleSeed + Time.frameCount);
            _deckQueue = new Queue<int>(order);
        }

        public void DrawOne()
        {
            if (_deckQueue == null || _deckQueue.Count == 0)
            {
                Debug.LogWarning("DeckDrawController: deck is empty.");
                return;
            }
            if (typeManager == null || handZone == null || deckSpawnPoint == null)
            {
                Debug.LogError("DeckDrawController: references not set.");
                return;
            }

            int cardId = _deckQueue.Dequeue();
            if (!CardDatabase.TryGet(cardId, out var data))
            {
                Debug.LogWarning($"DeckDrawController: Card ID {cardId} not found in database.");
                return;
            }

            var cardObj = typeManager.CreateCardByType(data.CA_Type, null);
            if (cardObj == null)
            {
                Debug.LogError("DeckDrawController: failed to instantiate card prefab.");
                return;
            }

            var view = cardObj.GetComponent<CardView3D>();
            if (view == null)
            {
                Debug.LogError("DeckDrawController: instantiated prefab missing CardView3D.");
                Destroy(cardObj);
                return;
            }

            view.Bind(data);

            cardObj.layer = LayerMask.NameToLayer("Default");
            foreach (var r in cardObj.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            foreach (var col in cardObj.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = true;
                col.isTrigger = false;
            }

            Quaternion sourceRot = deckInitialOrientation != null ? deckInitialOrientation.rotation : deckSpawnPoint.rotation;
            Vector3 sourceEuler = sourceRot.eulerAngles;
            Quaternion spawnRot = Quaternion.Euler(90f, sourceEuler.y - 27f, 0f);
            Quaternion spawnRotFlipped = spawnRot * Quaternion.AngleAxis(180f, Vector3.up);
            cardObj.transform.position = deckSpawnPoint.position;
            cardObj.transform.rotation = spawnRotFlipped;

            bool hasRoom = handZone.GetActiveCardCount() < maxHandCards;
            if (hasRoom)
            {
                cardObj.transform.SetParent(handZone.transform, true);
                handZone.RegisterCard(view);
                handZone.RealignCards(view, repositionExisting: true);
                StartCoroutine(ReturnWithFlip(view, spawnRot));
            }
            else
            {
                if (overflowEventZone == null)
                {
                    Debug.LogWarning("DeckDrawController: hand full and no overflowEventZone set. Destroying card.");
                    Destroy(cardObj);
                    return;
                }

                cardObj.transform.SetParent(null, true);
                view.PlayEventSequence(overflowEventZone);
            }
        }

        private System.Collections.IEnumerator ReturnWithFlip(CardView3D card, Quaternion spawnRot)
        {
            if (card == null) yield break;

            yield return null; // wait registration to settle

            int targetSlot = card.slotIndex >= 0 ? card.slotIndex : Mathf.Max(0, handZone.slots - 1);
            Vector3 homePos = handZone.GetSlotWorldPosition(targetSlot);
            Quaternion homeRot = handZone.GetSlotWorldRotation(targetSlot);
            card.SetHomeFromZone(handZone.transform, homePos, homeRot);

            float homeYaw = homeRot.eulerAngles.y;
            Quaternion baseRot = Quaternion.Euler(90f, homeYaw, 0f);

            Quaternion flipStartRot = spawnRot * Quaternion.AngleAxis(180f, Vector3.up);
            card.transform.rotation = flipStartRot;

            float phase1 = Mathf.Max(0.01f, handZone.returnPhase1);
            float phase2 = Mathf.Max(0.01f, handZone.returnPhase2);
            AnimationCurve curve1 = handZone.returnPhase1Curve;
            AnimationCurve curve2 = handZone.returnPhase2Curve;

            Vector3 startPos = deckSpawnPoint.position;
            Vector3 forward = handZone.transform.forward;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 aheadPos = homePos + forward * handZone.returnAheadZ;
            aheadPos.y = Mathf.Max(homePos.y, startPos.y);

            float flipThreshold = 0.7f;
            float t = 0f;
            while (t < phase1)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / phase1);
                float w = curve1 != null ? curve1.Evaluate(u) : u;
                Vector3 pos = Vector3.LerpUnclamped(startPos, aheadPos, w);
                float earlyFactor = Mathf.Clamp01(u / Mathf.Max(0.01f, flipThreshold));
                Quaternion rot = Quaternion.Slerp(flipStartRot, baseRot, earlyFactor);
                card.transform.SetPositionAndRotation(pos, rot);
                yield return null;
            }

            Vector3 phase2StartPos = card.transform.position;
            Quaternion phase2StartRot = card.transform.rotation;

            t = 0f;
            while (t < phase2)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / phase2);
                float w = curve2 != null ? curve2.Evaluate(u) : u;
                Vector3 pos = Vector3.LerpUnclamped(phase2StartPos, homePos, w);
                Quaternion rot = Quaternion.Slerp(phase2StartRot, baseRot, Mathf.Clamp01(w));
                card.transform.SetPositionAndRotation(pos, rot);
                yield return null;
            }

            card.transform.SetPositionAndRotation(homePos, baseRot);
        }
    }
}
