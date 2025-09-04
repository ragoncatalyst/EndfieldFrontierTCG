using System.Collections.Generic;
using EndfieldFrontierTCG.CA;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EndfieldFrontierTCG.Deck
{
    public class DeckBuilderController : MonoBehaviour
    {
        [Header("Left: Collection Grid (4 columns)")]
        public Transform CollectionGridRoot;
        public GameObject CollectionItemPrefab;

        [Header("Right: Deck Grid (1 column)")]
        public Transform DeckGridRoot;
        public GameObject DeckItemPrefab;

        [Header("Data")] public List<CollectionEntry> PlayerCollection = new List<CollectionEntry>();
        public DeckList CurrentDeck = new DeckList();

        private void Start()
        {
            RefreshUI();
        }

        public void RefreshUI()
        {
            foreach (Transform c in CollectionGridRoot) Destroy(c.gameObject);
            foreach (Transform c in DeckGridRoot) Destroy(c.gameObject);

            CardDatabase.EnsureLoaded();

            // Sort collection: cost asc then id
            var sortedCollection = new List<CollectionEntry>(PlayerCollection);
            sortedCollection.Sort((a, b) =>
            {
                int costA = a.CA_DPCost;
                int costB = b.CA_DPCost;
                if (costA != costB) return costA.CompareTo(costB);
                return a.CA_ID.CompareTo(b.CA_ID);
            });

            foreach (var ce in sortedCollection)
            {
                var go = Instantiate(CollectionItemPrefab, CollectionGridRoot);
                var text = go.GetComponentInChildren<TMP_Text>();
                if (CardDatabase.TryGet(ce.CA_ID, out var data))
                {
                    text.text = $"{data.CA_Name_DIS}  x{ce.CA_AvailableNum}  [{data.CA_DPCost}]";
                }
                else
                {
                    text.text = $"ID {ce.CA_ID}  x{ce.CA_AvailableNum}  [{ce.CA_DPCost}]";
                }

                var btn = go.GetComponentInChildren<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => AddToDeck(ce.CA_ID));
                }
            }

            var sortedDeck = CurrentDeck.SortedByCostThenId();
            foreach (var de in sortedDeck)
            {
                var go = Instantiate(DeckItemPrefab, DeckGridRoot);
                var text = go.GetComponentInChildren<TMP_Text>();
                if (CardDatabase.TryGet(de.CA_ID, out var data))
                {
                    text.text = $"{data.CA_Name_DIS}  x{de.Count}  [{data.CA_DPCost}]";
                }
                else
                {
                    text.text = $"ID {de.CA_ID}  x{de.Count}";
                }
            }
        }

        public void AddToDeck(int caId)
        {
            var entry = CurrentDeck.Entries.Find(e => e.CA_ID == caId);
            if (entry == null)
            {
                entry = new DeckEntry { CA_ID = caId, Count = 0 };
                CurrentDeck.Entries.Add(entry);
            }
            entry.Count++;
            RefreshUI();
        }
    }
}


