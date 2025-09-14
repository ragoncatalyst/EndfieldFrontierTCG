using UnityEngine;
using EndfieldFrontierTCG.CA;

namespace EndfieldFrontierTCG.Board
{
    [DisallowMultipleComponent]
    public class CardDragDropManager : MonoBehaviour
    {
        [Header("Drag Settings")]
        [Tooltip("拖拽时的高度")] 
        public float dragHeight = 1f;
        
        [Tooltip("拖拽时的旋转速度")] 
        public float rotationSpeed = 360f;
        
        [Tooltip("放置检测的射线层级")] 
        public LayerMask placementLayers = -1;

        private CardView3D _draggedCard;
        private CardSlotBehaviour _sourceSlot;
        private CardSlotBehaviour _targetSlot;
        private Vector3 _dragOffset;
        private bool _isDragging;

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                TryStartDrag();
            }
            else if (_isDragging)
            {
                if (Input.GetMouseButton(0))
                {
                    UpdateDrag();
                }
                else
                {
                    EndDrag();
                }
            }
        }

        private void TryStartDrag()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 1000f, placementLayers))
            {
                var card = hit.collider.GetComponent<CardView3D>();
                if (card != null)
                {
                    _draggedCard = card;
                    _sourceSlot = hit.collider.GetComponentInParent<CardSlotBehaviour>();
                    
                    if (_sourceSlot != null)
                        _sourceSlot.RemoveCard();

                    // 计算拖拽偏移
                    _dragOffset = _draggedCard.transform.position - hit.point;
                    _dragOffset.y = dragHeight;

                    _isDragging = true;
                    _draggedCard.transform.SetParent(null);
                }
            }
        }

        private void UpdateDrag()
        {
            if (_draggedCard == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Plane dragPlane = new Plane(Vector3.up, Vector3.up * dragHeight);
            float enter;

            if (dragPlane.Raycast(ray, out enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                _draggedCard.transform.position = hitPoint + _dragOffset;

                // 保持卡牌的原始旋转
                Quaternion targetRotation = _draggedCard.transform.rotation;
                _draggedCard.transform.rotation = targetRotation;

                // 更新目标槽位
                UpdateTargetSlot();
            }
        }

        private void UpdateTargetSlot()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            CardSlotBehaviour newTargetSlot = null;
            if (Physics.Raycast(ray, out hit, 1000f, placementLayers))
            {
                newTargetSlot = hit.collider.GetComponent<CardSlotBehaviour>();
            }

            // 更新高亮状态
            if (_targetSlot != newTargetSlot)
            {
                if (_targetSlot != null)
                    _targetSlot.UpdateVisualState(false);
                
                _targetSlot = newTargetSlot;
                
                if (_targetSlot != null)
                    _targetSlot.UpdateVisualState(true, _targetSlot.IsOccupied);
            }
        }

        private void EndDrag()
        {
            if (_draggedCard == null) return;

            bool placed = false;
            if (_targetSlot != null && !_targetSlot.IsOccupied)
            {
                placed = _targetSlot.TryPlaceCard(_draggedCard);
            }

            if (!placed && _sourceSlot != null)
            {
                _sourceSlot.TryPlaceCard(_draggedCard);
            }

            // 清理状态
            if (_targetSlot != null)
                _targetSlot.UpdateVisualState(false);

            _draggedCard = null;
            _sourceSlot = null;
            _targetSlot = null;
            _isDragging = false;
        }
    }
}
