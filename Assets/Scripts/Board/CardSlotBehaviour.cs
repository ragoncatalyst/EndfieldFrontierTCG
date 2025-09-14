using UnityEngine;
using EndfieldFrontierTCG.CA;

namespace EndfieldFrontierTCG.Board
{
    [DisallowMultipleComponent]
    public class CardSlotBehaviour : MonoBehaviour
    {
        [Header("Visual Settings")]
        [Tooltip("卡槽的视觉指示器")] 
        public MeshRenderer slotIndicator;
        
        [Tooltip("普通状态颜色")] 
        public Color normalColor = new Color(1f, 1f, 1f, 0.1f);  // 降低默认透明度
        
        [Tooltip("高亮状态颜色")] 
        public Color highlightColor = new Color(1f, 1f, 0f, 0.3f);
        
        [Tooltip("无效状态颜色")] 
        public Color invalidColor = new Color(1f, 0f, 0f, 0.3f);

        [Header("Size Settings")]
        [Tooltip("卡槽大小")] 
        public Vector2 slotSize = new Vector2(0.7f, 1f);  // 默认卡牌大小

        // 当前槽位状态
        private CardView3D _currentCard;
        private bool _isHighlighted;
        private BoxCollider _collider;

        private void Awake()
        {
            // 确保有BoxCollider
            _collider = GetComponent<BoxCollider>();
            if (_collider == null)
            {
                _collider = gameObject.AddComponent<BoxCollider>();
            }

            // 设置碰撞箱大小
            _collider.size = new Vector3(slotSize.x, 0.1f, slotSize.y);  // 增加Y轴高度以便更容易检测
            _collider.isTrigger = false;  // 不设为触发器，这样可以被射线检测到

            // 设置视觉指示器大小和颜色
            if (slotIndicator != null)
            {
                // 设置视觉指示器的缩放以匹配碰撞箱
                slotIndicator.transform.localScale = new Vector3(slotSize.x, slotSize.y, 1f);
                
                // 设置初始颜色
                UpdateVisualState(false);
            }
        }

        public void UpdateVisualState(bool highlight, bool invalid = false)
        {
            if (slotIndicator == null) return;
            
            _isHighlighted = highlight;
            Color targetColor = invalid ? invalidColor : (highlight ? highlightColor : normalColor);
            
            // 更新材质颜色
            var mat = slotIndicator.material;
            if (mat != null)
            {
                // 支持both Standard和URP材质
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", targetColor);
                else if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", targetColor);
            }
        }

        public bool TryPlaceCard(CardView3D card)
        {
            Debug.Log($"[CardSlotBehaviour] 开始尝试放置卡牌到槽位 {name}");
            Debug.Log($"[CardSlotBehaviour] 卡牌当前父物体: {(card.transform.parent != null ? card.transform.parent.name : "null")}");
            
            if (card == null || _currentCard != null) 
            {
                Debug.LogWarning($"[CardSlotBehaviour] 放置失败: card={card}, currentCard={_currentCard}");
                return false;
            }

            // 保存原始父物体，以便失败时恢复
            Transform originalParent = card.transform.parent;
            Vector3 originalPos = card.transform.position;
            Quaternion originalRot = card.transform.rotation;

            try
            {
                _currentCard = card;
                
                // 先从原来的父物体中移除
                card.transform.SetParent(null);
                
                // 设置新的父物体
                card.transform.SetParent(transform, true);
                
                // 使用卡牌的 SnapTo 方法来设置位置和旋转
                Vector3 targetPos = transform.position;
                Quaternion targetRot = transform.rotation * Quaternion.Euler(-90f, 0f, 0f); // 调整为卡牌的正确朝向
                Debug.Log($"[CardSlotBehaviour] 设置卡牌旋转: {targetRot.eulerAngles}");
                card.SnapTo(targetPos, targetRot);
                
                // 确保卡牌的状态正确
                if (card.GetComponent<Rigidbody>() is Rigidbody rb)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }
                
                Debug.Log($"[CardSlotBehaviour] 成功放置卡牌到槽位: {name}");
                Debug.Log($"[CardSlotBehaviour] 卡牌新父物体: {card.transform.parent.name}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[CardSlotBehaviour] 放置过程中发生错误: {e}");
                // 恢复原始状态
                _currentCard = null;
                card.transform.SetParent(originalParent, true);
                card.transform.position = originalPos;
                card.transform.rotation = originalRot;
                return false;
            }
        }

        public CardView3D RemoveCard()
        {
            var card = _currentCard;
            if (card != null)
            {
                _currentCard = null;
                card.transform.SetParent(null);
            }
            return card;
        }

        public bool IsOccupied => _currentCard != null;
        public bool IsHighlighted => _isHighlighted;

        public bool CanAcceptCard(CardView3D card)
        {
            if (card == null || IsOccupied) return false;
            
            // 这里可以添加更多的检查逻辑，比如：
            // - 卡牌类型是否符合槽位要求
            // - 玩家是否有足够的资源放置卡牌
            // - 是否满足特殊的游戏规则
            
            return true;
        }

        public Vector3 GetCardPosition()
        {
            // 返回卡牌应该放置的精确位置
            return transform.position;
        }

        public Quaternion GetCardRotation()
        {
            // 返回卡牌应该旋转到的精确角度
            // 1. 旋转-90度让卡牌平放并正面朝上
            // 2. 不需要Y轴旋转，保持卡牌正面朝向摄像机
            // 3. 最后根据槽位的朝向旋转
            return transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
        }

        public void OnHoverEnter()
        {
            UpdateVisualState(true);
        }

        public void OnHoverExit()
        {
            UpdateVisualState(false);
        }
    }
}