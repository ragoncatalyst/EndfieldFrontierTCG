using System;
using System.Collections.Generic;
using System.Linq;
using EndfieldFrontierTCG.CA;

namespace EndfieldFrontierTCG.Deck
{
    public static class DeckShuffler
    {
        public enum CostBucket { Low, Mid, High }

        public static Dictionary<CostBucket, List<int>> Bucketize(DeckList deck)
        {
            CardDatabase.EnsureLoaded();
            var list = new List<(int id, int cost, int count)>();
            foreach (var (entry, data) in deck.EnumerateWithData())
            {
                list.Add((entry.CA_ID, data.CA_DPCost, entry.Count));
            }

            if (list.Count == 0)
            {
                return new Dictionary<CostBucket, List<int>>
                {
                    { CostBucket.Low, new List<int>() },
                    { CostBucket.Mid, new List<int>() },
                    { CostBucket.High, new List<int>() }
                };
            }

            int minCost = list.Min(x => x.cost);
            int maxCost = list.Max(x => x.cost);
            if (minCost == maxCost)
            {
                // All same cost: treat as Mid for simplicity
                return ExpandByCount(list.ToDictionary(x => x.id, x => CostBucket.Mid));
            }

            // Dynamic thresholds by range thirds (example aligned with spec idea)
            float range = maxCost - minCost;
            float lowUpper = minCost + range / 3f;
            float midUpper = minCost + 2f * range / 3f;

            var map = new Dictionary<int, CostBucket>();
            foreach (var item in list)
            {
                if (item.cost <= Math.Floor(lowUpper)) map[item.id] = CostBucket.Low;
                else if (item.cost <= Math.Floor(midUpper)) map[item.id] = CostBucket.Mid;
                else map[item.id] = CostBucket.High;
            }

            return ExpandByCount(map, list);
        }

        private static Dictionary<CostBucket, List<int>> ExpandByCount(Dictionary<int, CostBucket> idToBucket, List<(int id, int cost, int count)> src = null)
        {
            var result = new Dictionary<CostBucket, List<int>>
            {
                { CostBucket.Low, new List<int>() },
                { CostBucket.Mid, new List<int>() },
                { CostBucket.High, new List<int>() }
            };

            if (src == null)
            {
                foreach (var kv in idToBucket)
                {
                    result[kv.Value].Add(kv.Key);
                }
            }
            else
            {
                foreach (var it in src)
                {
                    for (int i = 0; i < it.count; i++)
                    {
                        result[idToBucket[it.id]].Add(it.id);
                    }
                }
            }
            return result;
        }

        public static List<int> ShuffleWithBuckets(DeckList deck, int seed)
        {
            var buckets = Bucketize(deck);
            var rng = new Random(seed);

            // Early, mid, late phases: interleave low/mid/high with weights
            var final = new List<int>();

            // Helper: shuffle a list using a provided RNG
            void ShuffleInPlaceWithRng(List<int> list, Random r)
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = r.Next(i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }

            // Perform a couple of passes with derived seeds to increase sensitivity to seed changes
            ShuffleInPlaceWithRng(buckets[CostBucket.Low], new Random(seed ^ unchecked((int)0x9E3779B9)));
            ShuffleInPlaceWithRng(buckets[CostBucket.Low], new Random(seed));

            ShuffleInPlaceWithRng(buckets[CostBucket.Mid], new Random(seed ^ unchecked((int)0xC13FA9A9)));
            ShuffleInPlaceWithRng(buckets[CostBucket.Mid], new Random(seed));

            ShuffleInPlaceWithRng(buckets[CostBucket.High], new Random(seed ^ unchecked((int)0x7F4A7C15)));
            ShuffleInPlaceWithRng(buckets[CostBucket.High], new Random(seed));

            int total = buckets[CostBucket.Low].Count + buckets[CostBucket.Mid].Count + buckets[CostBucket.High].Count;
            for (int index = 0; index < total; index++)
            {
                float phase = total == 0 ? 0 : (float)index / total;
                List<CostBucket> pref;
                if (phase < 0.33f) pref = new List<CostBucket> { CostBucket.Low, CostBucket.Mid, CostBucket.High };
                else if (phase < 0.66f) pref = new List<CostBucket> { CostBucket.Mid, CostBucket.Low, CostBucket.High };
                else pref = new List<CostBucket> { CostBucket.High, CostBucket.Mid, CostBucket.Low };

                // Choose among available preferred buckets using RNG so seed changes produce different interleavings.
                var available = pref.Where(b => buckets[b].Count > 0).ToList();
                if (available.Count == 0)
                {
                    // fallback: find any non-empty bucket
                    available = new List<CostBucket>();
                    foreach (var b in (CostBucket[])Enum.GetValues(typeof(CostBucket)))
                        if (buckets[b].Count > 0) available.Add(b);
                    if (available.Count == 0) break;
                }

                int pickBucketIndex = rng.Next(available.Count);
                var pickBucket = available[pickBucketIndex];
                var listRef = buckets[pickBucket];
                int pickIndex = rng.Next(listRef.Count);
                int picked = listRef[pickIndex];
                listRef.RemoveAt(pickIndex);
                final.Add(picked);
            }

            return final;
        }

        public static List<int> DrawStartingHand(DeckList deck, int seed)
        {
            var buckets = Bucketize(deck);
            int lowCount = buckets[CostBucket.Low].Count;

            var rng = new Random(seed);
            int neededLow = 0;
            if (lowCount >= 8)
            {
                neededLow = rng.NextDouble() < 0.75 ? 3 : 2;
            }
            else if (lowCount >= 6)
            {
                neededLow = rng.NextDouble() < 0.60 ? 2 : 1;
            }
            else
            {
                neededLow = rng.NextDouble() < 0.80 ? 1 : 0;
            }

            var deckShuffled = ShuffleWithBuckets(deck, seed + 1337);
            var hand = new List<int>();

            // Try to meet low-cost requirement first
            for (int i = 0; i < deckShuffled.Count && hand.Count < 5; i++)
            {
                int id = deckShuffled[i];
                if (neededLow > 0 && buckets[CostBucket.Low].Contains(id))
                {
                    hand.Add(id);
                    buckets[CostBucket.Low].Remove(id);
                    neededLow--;
                }
            }

            // Fill remaining slots with next cards
            foreach (var id in deckShuffled)
            {
                if (hand.Count >= 5) break;
                if (!hand.Contains(id)) hand.Add(id);
            }

            return hand;
        }
    }
}


