using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// ブラウザとのやり取り（クリップボード・ページの URL）。WebGL 以外では Unity の標準機能で代用する。
    /// </summary>
    public static class WebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int GS_CopyText(string text);
#endif

        /// <summary>文字列をクリップボードへ。失敗したら false。</summary>
        public static bool Copy(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return GS_CopyText(text) != 0;
#else
            GUIUtility.systemCopyBuffer = text;
            return true;
#endif
        }

        /// <summary>この部屋に直接入れるリンク。ブラウザで動いていないときは null。</summary>
        public static string InviteUrl(string roomCode)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return InviteUrl(Application.absoluteURL, roomCode);
#else
            return null;
#endif
        }

        public static string InviteUrl(string pageUrl, string roomCode)
        {
            if (string.IsNullOrEmpty(pageUrl)) return null;
            int cut = pageUrl.IndexOfAny(new[] { '?', '#' });
            string baseUrl = cut >= 0 ? pageUrl.Substring(0, cut) : pageUrl;
            return $"{baseUrl}?room={roomCode}";
        }

        /// <summary>ページの URL に ?room=1234 があれば、その部屋番号。</summary>
        public static string RoomFromPage()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return RoomFromUrl(Application.absoluteURL);
#else
            return null;
#endif
        }

        public static string RoomFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int q = url.IndexOf('?');
            if (q < 0) return null;
            int hash = url.IndexOf('#', q);
            string query = hash >= 0 ? url.Substring(q + 1, hash - q - 1) : url.Substring(q + 1);
            foreach (var part in query.Split('&'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2 || kv[0] != "room") continue;
                string code = Uri.UnescapeDataString(kv[1]).Trim();
                if (code.Length != 4) return null;
                foreach (var c in code) if (c < '0' || c > '9') return null;
                return code;
            }
            return null;
        }
    }
}
