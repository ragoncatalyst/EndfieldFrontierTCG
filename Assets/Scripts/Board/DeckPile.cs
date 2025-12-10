using UnityEngine;

namespace EndfieldFrontierTCG.Board
{
    public class DeckPile : MonoBehaviour
    {
        public Vector3 tiltEuler = new Vector3(90f, 30f, 0f);
        public float yHeight = 0f;
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.8f, 0.8f, 0.8f, 0.5f);
            Gizmos.DrawCube(transform.position + Vector3.up * yHeight, new Vector3(1f, 0f, 1.6f));
        }
    }
}


