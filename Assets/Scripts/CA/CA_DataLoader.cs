using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace EndfieldFrontierTCG.CA
{
    [Serializable]
    public class CardData
    {
        public int CA_ID;
        public string CA_Name_DIS;
        public string CA_Type;
        public int CA_HPMaximum;
        public int CA_ATK_INI;
        public string CA_EffectInfo_DIS;
        public int CA_DPCost;
        public string CA_MainImage;

        public override string ToString()
        {
            return $"[CardData] ID={CA_ID} Name={CA_Name_DIS} Type={CA_Type} Cost={CA_DPCost}";
        }
    }

    public static class CardDatabase
    {
        private static readonly Dictionary<int, CardData> _idToCardData = new Dictionary<int, CardData>();
        private static bool _isLoaded;

        public static IReadOnlyDictionary<int, CardData> All => _idToCardData;

        public static void EnsureLoaded()
        {
            if (_isLoaded) return;
            LoadFromResources();
        }

        private static void LoadFromResources()
        {
            var textAsset = Resources.Load<TextAsset>("CA_Data");
            if (textAsset == null)
            {
                Debug.LogError("CA_Data.csv not found in Resources. Expected at Assets/Resources/CA_Data.csv");
                _isLoaded = true;
                return;
            }

            _idToCardData.Clear();

            var lines = textAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            bool isHeaderSkipped = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!isHeaderSkipped)
                {
                    isHeaderSkipped = true;
                    continue;
                }

                var cols = SplitCsv(line).ToArray();
                if (cols.Length < 8)
                {
                    Debug.LogWarning($"CA_Data row has insufficient columns: {line}");
                    continue;
                }

                try
                {
                    var data = new CardData
                    {
                        CA_ID = ParseInt(cols[0]),
                        CA_Name_DIS = cols[1],
                        CA_Type = cols[2],
                        CA_HPMaximum = ParseInt(cols[3]),
                        CA_ATK_INI = ParseInt(cols[4]),
                        CA_EffectInfo_DIS = cols[5],
                        CA_DPCost = ParseInt(cols[6]),
                        CA_MainImage = cols[7]
                    };

                    _idToCardData[data.CA_ID] = data;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to parse CA_Data row: {line}. Error: {ex}");
                }
            }

            _isLoaded = true;
        }

        private static IEnumerable<string> SplitCsv(string line)
        {
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();
            foreach (var ch in line)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (ch == ',' && !inQuotes)
                {
                    yield return current.ToString();
                    current.Length = 0;
                }
                else
                {
                    current.Append(ch);
                }
            }
            yield return current.ToString();
        }

        private static int ParseInt(string s)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            return 0;
        }

        public static bool TryGet(int caId, out CardData data)
        {
            EnsureLoaded();
            return _idToCardData.TryGetValue(caId, out data);
        }
    }
}


