// WebGL bridge: speak a death-roast out loud using the browser's built-in
// speech synthesis. Called from Voice.cs. Best-effort only — never let it break
// the game.
//
// TONE: this used to be pitched DOWN and slowed (pitch 0.4 / rate 0.95) for a
// "demonic vampire". That was the wrong read — slow and deep on a one-word line
// ("Cooked.") sounds like a bored answering machine, not a taunt. The roasts are
// now one to three words, so the delivery has to be FAST and BRIGHT: it lands
// like a comment being read out, which is the register the writing is in. Rate
// is pushed well past normal because short lines need to be over before the
// player has finished respawning.
mergeInto(LibraryManager.library, {
  TI_Speak: function (textPtr, volume) {
    try {
      if (typeof window === 'undefined' || !window.speechSynthesis) return;
      var text = UTF8ToString(textPtr);
      if (!text) return;
      window.speechSynthesis.cancel();            // drop any previous line
      var u = new SpeechSynthesisUtterance(text);
      // Jitter both per line so the same roast twice never sounds identical —
      // a fixed pitch is what made the old voice feel like a robot reading.
      u.pitch = 1.05 + Math.random() * 0.35;      // bright, a little smug
      u.rate  = 1.30 + Math.random() * 0.20;      // snappy — done in under a second
      u.volume = Math.max(0, Math.min(1, volume)); // honour the VOICE slider
      // Prefer a natural, non-robotic English voice. Browsers ship a wide mix and
      // the DEFAULT is often the flattest one available, so the good ones are
      // named explicitly and the obviously synthetic legacy voices are skipped.
      var voices = window.speechSynthesis.getVoices() || [];
      var want = ['google us english', 'samantha', 'aria', 'jenny', 'guy',
                  'google uk english male', 'daniel', 'alex'];
      var best = null, bestRank = 999;
      for (var i = 0; i < voices.length; i++) {
        var v = voices[i];
        if (!v.lang || v.lang.indexOf('en') !== 0) continue;
        var n = (v.name || '').toLowerCase();
        for (var w = 0; w < want.length; w++) {
          if (n.indexOf(want[w]) >= 0 && w < bestRank) { best = v; bestRank = w; }
        }
        // Any local English voice beats nothing at all.
        if (best === null) best = v;
      }
      if (best) u.voice = best;
      window.speechSynthesis.speak(u);
    } catch (e) {
      // ignore — TTS is a nice-to-have
    }
  }
});
