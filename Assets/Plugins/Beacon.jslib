// WebGL bridge: hand a final batch of analytics events to the browser's
// navigator.sendBeacon, which the browser delivers even as the tab is closing
// (a normal fetch/XHR would be cancelled on unload). Called from Analytics.cs.
mergeInto(LibraryManager.library, {
  AnalyticsBeacon: function (urlPtr, bodyPtr) {
    try {
      var url = UTF8ToString(urlPtr);
      var body = UTF8ToString(bodyPtr);
      var blob = new Blob([body], { type: 'text/plain' });
      navigator.sendBeacon(url, blob);
    } catch (e) {
      // best-effort only — never let analytics break the game
    }
  }
});
