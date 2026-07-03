// WebGL bridge: share a PNG result card. Uses the Web Share API with a file when
// available (mobile / supporting browsers), otherwise downloads the PNG and copies
// the brag text to the clipboard. Called from ShareCard.cs. Best-effort only.
mergeInto(LibraryManager.library, {
  TI_Share: function (namePtr, b64Ptr, textPtr) {
    try {
      var name = UTF8ToString(namePtr);
      var b64  = UTF8ToString(b64Ptr);
      var text = UTF8ToString(textPtr);
      var bin = atob(b64), len = bin.length, bytes = new Uint8Array(len);
      for (var i = 0; i < len; i++) bytes[i] = bin.charCodeAt(i);
      var blob = new Blob([bytes], { type: 'image/png' });
      var file = new File([blob], name, { type: 'image/png' });

      if (navigator.canShare && navigator.canShare({ files: [file] })) {
        navigator.share({ files: [file], text: text, title: 'Trust Issues' }).catch(function () {});
      } else {
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url; a.download = name;
        document.body.appendChild(a); a.click(); document.body.removeChild(a);
        URL.revokeObjectURL(url);
        if (navigator.clipboard && text) navigator.clipboard.writeText(text).catch(function () {});
      }
    } catch (e) { /* sharing is a nice-to-have */ }
  },

  // Share a plain link + text (the curse links). Web Share API when available,
  // else copy "text url" to the clipboard. Best-effort, like TI_Share.
  TI_ShareLink: function (urlPtr, textPtr) {
    try {
      var url = UTF8ToString(urlPtr);
      var text = UTF8ToString(textPtr);
      if (navigator.share) {
        navigator.share({ url: url, text: text, title: 'Trust Issues' }).catch(function () {});
      } else if (navigator.clipboard) {
        navigator.clipboard.writeText(text + ' ' + url).catch(function () {});
      }
    } catch (e) { /* sharing is a nice-to-have */ }
  }
});
