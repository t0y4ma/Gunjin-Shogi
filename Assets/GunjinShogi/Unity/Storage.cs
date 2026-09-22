using System;
using System.Collections.Generic;
using GunjinShogi.Core;
using UnityEngine;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 端末に保存する設定。PlayerPrefs は WebGL ではブラウザの IndexedDB に保存される。
    /// ブラウザのデータ削除やシークレットモードでは消えるので、大事なものはコードで控えてもらう。
    /// </summary>
    public static class AppSettings
    {
        const string RulesKey = "gs.rules";
        const string DisplayKey = "gs.display";
        const string LevelKey = "gs.cpuLevel";

        public static StandardRuleOptions Rules
        {
            get
            {
                var code = PlayerPrefs.GetString(RulesKey, "");
                return RuleCodec.TryDecode(code, out var o) ? o : new StandardRuleOptions();
            }
            set
            {
                PlayerPrefs.SetString(RulesKey, RuleCodec.Encode(value));
                PlayerPrefs.Save();
            }
        }

        public static DisplaySettings Display
        {
            get => DisplaySettings.FromBits(PlayerPrefs.GetInt(DisplayKey, DisplaySettings.DefaultBits));
            set { PlayerPrefs.SetInt(DisplayKey, value.ToBits()); PlayerPrefs.Save(); }
        }

        public static int CpuLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(LevelKey, 1), 0, 2);
            set { PlayerPrefs.SetInt(LevelKey, value); PlayerPrefs.Save(); }
        }
    }

    /// <summary>表示と操作の設定（対局のルールには影響しない）。</summary>
    public sealed class DisplaySettings
    {
        public bool HighlightLastMove = true;
        public bool ScrollableLog = true;
        public bool MemoEnabled = true;
        public bool Assist;

        public static int DefaultBits => new DisplaySettings().ToBits();

        public int ToBits() =>
            (HighlightLastMove ? 1 : 0) | (ScrollableLog ? 2 : 0) | (MemoEnabled ? 4 : 0) | (Assist ? 8 : 0);

        public static DisplaySettings FromBits(int b) => new DisplaySettings
        {
            HighlightLastMove = (b & 1) != 0,
            ScrollableLog = (b & 2) != 0,
            MemoEnabled = (b & 4) != 0,
            Assist = (b & 8) != 0,
        };

        public DisplaySettings Clone() => FromBits(ToBits());
    }

    public sealed class PresetSlot
    {
        public int Index;
        public string Code;      // SetupCodec の文字列。空なら未使用
        public string SavedAt;
        public bool IsEmpty => string.IsNullOrEmpty(Code);
    }

    /// <summary>配置プリセットの保存先。差し替えられるようにインターフェースにしておく。</summary>
    public interface IPresetStorage
    {
        IReadOnlyList<PresetSlot> Load(string compositionKey);
        void Save(string compositionKey, int slot, string code);
    }

    /// <summary>
    /// PlayerPrefs への保存。キーに駒構成（盤の形と駒の枚数）を含めるので、
    /// 構成が違うルールの配置は候補に出ない。
    /// </summary>
    public sealed class PlayerPrefsPresetStorage : IPresetStorage
    {
        public const int SlotCount = 5;
        const int SchemaVersion = 1;

        static string Key(string comp, int slot) => $"gs.preset.v{SchemaVersion}.{comp}.{slot}";

        public IReadOnlyList<PresetSlot> Load(string compositionKey)
        {
            var list = new List<PresetSlot>();
            for (int i = 0; i < SlotCount; i++)
            {
                var raw = PlayerPrefs.GetString(Key(compositionKey, i), "");
                var parts = raw.Split('|');
                list.Add(new PresetSlot
                {
                    Index = i,
                    Code = parts.Length > 0 ? parts[0] : "",
                    SavedAt = parts.Length > 1 ? parts[1] : "",
                });
            }
            return list;
        }

        public void Save(string compositionKey, int slot, string code)
        {
            PlayerPrefs.SetString(Key(compositionKey, slot), code + "|" + DateTime.Now.ToString("MM/dd HH:mm"));
            PlayerPrefs.Save();
        }
    }
}
