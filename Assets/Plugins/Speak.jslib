// WebGL bridge: speak a death-roast out loud using the browser's built-in
// speech synthesis. Pitched/slowed for a demonic vampire tone. Called from
// Voice.cs. Best-effort only — never let it break the game.
mergeInto(LibraryManager.library, {
  TI_Speak: function (textPtr, volume) {
    try {
      if (typeof window === 'undefined' || !window.speechSynthesis) return;
      var text = UTF8ToString(textPtr);
      if (!text) return;
      window.speechSynthesis.cancel();            // drop any previous line
      var u = new SpeechSynthesisUtterance(text);
      u.pitch = 0.4;                              // deep / demonic
      u.rate  = 0.95;
      u.volume = Math.max(0, Math.min(1, volume)); // honour the VOICE slider
      // Prefer a deeper English voice if one is available.
      var voices = window.speechSynthesis.getVoices();
      for (var i = 0; i < voices.length; i++) {
        var n = (voices[i].name || '').toLowerCase();
        if (voices[i].lang && voices[i].lang.indexOf('en') === 0 &&
            (n.indexOf('male') >= 0 || n.indexOf('daniel') >= 0 || n.indexOf('google uk') >= 0)) {
          u.voice = voices[i]; break;
        }
      }
      window.speechSynthesis.speak(u);
    } catch (e) {
      // ignore — TTS is a nice-to-have
    }
  }
});
