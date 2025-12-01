using System;
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
        [SerializeField] private DeckPileVisualizer pileVisualizer;

    [Header("Deck Definition")]
    [SerializeField] private List<DeckEntry> starterDeck = new List<DeckEntry>();
    // If 0, a random seed will be generated at runtime (per run).
    [SerializeField] private int shuffleSeed = 0;

        private Queue<int> _deckQueue;

        private void Awake()
        {
            // If seed left as 0 in inspector, pick a fresh random seed per run so different play sessions differ.
            if (shuffleSeed == 0)
            {
                try
                {
                    // Use Guid to get a high-entropy int seed
                    shuffleSeed = Guid.NewGuid().GetHashCode();
                }
                catch
                {
                    shuffleSeed = System.Environment.TickCount;
                }
                Debug.Log($"DeckDrawController: generated runtime shuffleSeed={shuffleSeed}");
            }

            BuildDeckQueue();
            UpdatePileVisual();
        }

        private void BuildDeckQueue()
        {
            CardDatabase.EnsureLoaded();

            if (starterDeck == null || starterDeck.Count == 0)
            {
                Debug.LogWarning("DeckDrawController: starterDeck is empty.");
                _deckQueue = new Queue<int>();
                UpdatePileVisual();
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
                UpdatePileVisual();
                return;
            }

            var order = DeckShuffler.ShuffleWithBuckets(deckList, shuffleSeed);
            _deckQueue = new Queue<int>(order);
            UpdatePileVisual();
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
            UpdatePileVisual();
        }

        public void DrawOne()
        {
            if (_deckQueue == null || _deckQueue.Count == 0)
            {
                Debug.LogWarning("DeckDrawController: deck is empty.");
                UpdatePileVisual();
                return;
            }
            if (typeManager == null || handZone == null || deckSpawnPoint == null)
            {
                Debug.LogError("DeckDrawController: references not set.");
                return;
            }

            int cardId = _deckQueue.Dequeue();
            UpdatePileVisual();
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
                handZone.ApplyTwoPhaseHome(view, out _, out _);
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
                Vector3 overflowAxis = (spawnRot * Vector3.up).normalized;
                Quaternion overflowTiltRot = Quaternion.AngleAxis(180f - 22f, overflowAxis) * spawnRot;
                cardObj.transform.rotation = overflowTiltRot;
                view.PlayEventSequence(overflowEventZone);
            }
        }

        private void UpdatePileVisual()
        {
            if (pileVisualizer == null) return;
            int count = _deckQueue != null ? _deckQueue.Count : 0;
            pileVisualizer.SetCount(count);
        }

        private System.Collections.IEnumerator ReturnWithFlip(CardView3D card, Quaternion spawnRot)
        {
            if (card == null) yield break;
            // Mark the card as 'returning' so other systems (HandSplineZone / CardView3D) won't
            // concurrently write its transform and cause jitter. We use reflection because
            // CardView3D.IsReturningHome has a private setter.
            System.Reflection.PropertyInfo isReturningProp = null;
            try
            {
                isReturningProp = card.GetType().GetProperty("IsReturningHome", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var setter = isReturningProp?.GetSetMethod(true);
                setter?.Invoke(card, new object[] { true });
            }
            catch { }

            yield return null; // wait registration to settle

            Vector3 homePos;
            Quaternion homeRot;
            handZone.ApplyTwoPhaseHome(card, out homePos, out homeRot);

            const float initialTiltDeg = 22f;
            Quaternion baseRot = homeRot;
            Vector3 localYAxis = (baseRot * Vector3.up).normalized;
            Quaternion flipStartRot = Quaternion.AngleAxis(180f - initialTiltDeg, localYAxis) * baseRot;
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

            const float flipStartProgress = 0.2f;
            float flipThreshold = Mathf.Max(flipStartProgress + 0.05f, 0.7f);
            float pathLength = Vector3.Distance(aheadPos, startPos);
            float t = 0f;
            while (t < phase1)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / phase1);
                float w = curve1 != null ? curve1.Evaluate(u) : u;
                Vector3 pos = Vector3.LerpUnclamped(startPos, aheadPos, w);
                float travelled = Vector3.Distance(pos, startPos);
                float progress = pathLength > 1e-4f ? Mathf.Clamp01(travelled / pathLength) : Mathf.Clamp01(w);
                float flipDenom = Mathf.Max(0.01f, flipThreshold - flipStartProgress);
                float earlyFactor = 0f;
                if (progress > flipStartProgress)
                {
                    earlyFactor = Mathf.Clamp01((progress - flipStartProgress) / flipDenom);
                }
                Quaternion rot = Quaternion.Slerp(flipStartRot, baseRot, earlyFactor);
                card.transform.SetPositionAndRotation(pos, rot);
                yield return null;
            }

            Vector3 phase2StartPos = card.transform.position;
            phase2StartPos.y = homePos.y;
            card.transform.position = new Vector3(card.transform.position.x, homePos.y, card.transform.position.z);
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

            // Clear the returning flag so other systems resume control
            try
            {
                var setter2 = isReturningProp?.GetSetMethod(true);
                setter2?.Invoke(card, new object[] { false });
            }
            catch { }
        }
    }
}
