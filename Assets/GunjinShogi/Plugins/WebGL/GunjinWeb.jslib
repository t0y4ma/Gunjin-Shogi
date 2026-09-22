// 軍人将棋：ブラウザ側の小さな補助（WebGL ビルドでだけ使われる）
mergeInto(LibraryManager.library, {
  // クリップボードへコピー。https なら Clipboard API、それ以外は選択してコピーする昔の方法
  GS_CopyText: function (ptr) {
    var text = UTF8ToString(ptr);
    function fallback() {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      var ok = false;
      try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
      document.body.removeChild(ta);
      return ok;
    }
    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).catch(function () { fallback(); });
      return 1;
    }
    return fallback() ? 1 : 0;
  }
});
