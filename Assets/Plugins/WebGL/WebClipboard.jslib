mergeInto(LibraryManager.library, {
  JsCopyToClipboard: function (strPtr) {
    var str = UTF8ToString(strPtr);
    try {
      if (window.isSecureContext && navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(str);
        return;
      }
    } catch (e) {
      // Fall through to execCommand fallback.
    }

    try {
      var ta = document.createElement('textarea');
      ta.value = str;
      ta.style.position = 'fixed';
      ta.style.top = '0';
      ta.style.left = '0';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.focus();
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    } catch (e) {
      console.warn('[WebClipboard] copy failed', e);
    }
  }
});
