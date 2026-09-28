using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// モーダルの暗い覆いの部分（カードの外側）をクリックしたら閉じる。
    /// 押し始めと離した位置の両方がカードの外のときだけ閉じる（カード内からのドラッグでは閉じない）。
    /// </summary>
    public sealed class ModalBackdrop : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        public RectTransform Card;
        public Action OnOutside;
        bool pressedOutside;

        bool Outside(PointerEventData e) =>
            Card == null || !RectTransformUtility.RectangleContainsScreenPoint(Card, e.position, e.pressEventCamera ?? e.enterEventCamera);

        public void OnPointerDown(PointerEventData e) => pressedOutside = Outside(e);

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (pressedOutside && Outside(e)) OnOutside?.Invoke();
            pressedOutside = false;
        }
    }
}
