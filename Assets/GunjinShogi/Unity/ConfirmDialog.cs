using System;
using TMPro;
using UnityEngine;

namespace GunjinShogi.UnityView
{
    /// <summary>「本当に〜しますか？」の確認。外側のクリックやキャンセルで閉じる（何もしない）。</summary>
    public sealed class ConfirmDialog
    {
        readonly GameObject root;
        readonly TextMeshProUGUI title, body;
        readonly UnityEngine.UI.Button ok;
        Action onOk;

        public ConfirmDialog(Transform canvas)
        {
            var (r, content) = Ui.Modal("Confirm", canvas, 0.42f, 0.36f);
            root = r;
            Ui.Column(content, 14);
            title = Ui.Text("Title", content, "", 38, Theme.Text, bold: true);
            Ui.Size(title, 54);
            Ui.AutoSize(title, 38);
            body = Ui.Text("Body", content, "", 26, Theme.TextMuted);
            Ui.Size(body).flexibleHeight = 1;
            Ui.AutoSize(body, 26);
            var row = Ui.Rect("Buttons", content);
            var h = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            h.spacing = 16;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            Ui.Size(row, 76);
            Ui.Button("Cancel", row, "キャンセル", Close, fontSize: 30);
            ok = Ui.Button("Ok", row, "", () => { var a = onOk; Close(); a?.Invoke(); }, Ui.ButtonStyle.Danger, 30);
            Ui.CloseOnBackdrop(root, Close);
        }

        public bool IsOpen => root.activeSelf;

        public void Show(string titleText, string bodyText, string okLabel, Action action)
        {
            title.text = titleText;
            body.text = bodyText;
            Ui.SetLabel(ok, okLabel);
            onOk = action;
            root.SetActive(true);
            root.transform.SetAsLastSibling();
        }

        public void Close()
        {
            onOk = null;
            root.SetActive(false);
        }

        /// <summary>縦画面では幅を広げる。</summary>
        public void ApplyOrientation(bool landscape)
        {
            var card = (RectTransform)root.transform.Find("Card");
            float w = landscape ? 0.42f : 0.86f, hgt = landscape ? 0.36f : 0.22f;
            Ui.Anchors(card, (1 - w) / 2, (1 - hgt) / 2, 1 - (1 - w) / 2, 1 - (1 - hgt) / 2);
        }
    }
}
