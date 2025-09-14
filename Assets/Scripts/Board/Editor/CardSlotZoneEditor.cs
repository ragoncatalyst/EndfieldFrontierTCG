using UnityEngine;
using UnityEditor;

namespace EndfieldFrontierTCG.Board
{
    [CustomEditor(typeof(CardSlotZone))]
    public class CardSlotZoneEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CardSlotZone zone = (CardSlotZone)target;

            if (GUILayout.Button("Force Reset Transform"))
            {
                // 记录撤销
                Undo.RecordObject(zone.transform, "Reset Transform");
                
                // 强制重置Transform
                zone.transform.position = new Vector3(0f, 0.01f, 0f);
                zone.transform.rotation = Quaternion.identity;
                zone.transform.localScale = Vector3.one;
                
                // 标记为已修改
                EditorUtility.SetDirty(zone.transform);
            }

            // 绘制默认Inspector
            DrawDefaultInspector();
        }
    }
}
