using System;
using System.Collections.Generic;
using System.Linq;
using EndfieldFrontierTCG.CA;

namespace EndfieldFrontierTCG.Deck
{
    [Serializable]
    public class CollectionEntry
    {
        public int CA_ID;
        public int CA_DPCost;
        public int CA_AvailableNum;
        public int CA_InDeckNum;
    }

    [Serializable]
    public class DeckEntry
    {
        public int CA_ID;
        public int Count;
    }

    [Serializable]
    public class DeckList
    {
        public string DeckName;
        public List<DeckEntry> Entries = new List<DeckEntry>();

        public int TotalCards => Entries.Sum(e => e.Count);

        public IEnumerable<(DeckEntry entry, CardData data)> EnumerateWithData()
        {
            CardDatabase.EnsureLoaded();
            foreach (var e in Entries)
            {
                if (CardDatabase.TryGet(e.CA_ID, out var data))
                    yield return (e, data);
            }
        }

        public List<DeckEntry> SortedByCostThenId()
        {
            CardDatabase.EnsureLoaded();
            return Entries
                .OrderBy(e => CardDatabase.TryGet(e.CA_ID, out var d) ? d.CA_DPCost : int.MaxValue)
                .ThenBy(e => e.CA_ID)
                .ToList();
        }
    }
}


