# Publishing Trust Issues to Google Play — the complete run-through

Everything below is in the order you'll actually do it. Anything marked
**[YOU]** is something I cannot do for you — it needs your Google account, your
payment card, or a password I must never handle. Everything marked **[DONE]** is
already set up in the project.

---

## Part 0 — What's already done

| Thing | State |
| --- | --- |
| Package name `com.shabbir.trustissues` | **[DONE]** — set in `Assets/Editor/BuildAndroid.cs` |
| 64-bit (ARM64) + IL2CPP — required by Play | **[DONE]** |
| Privacy policy text | **[DONE]** — `docs/PRIVACY_POLICY.md`, needs hosting (Part 3) |
| Analytics opt-out switch (the policy promises one) | **[DONE]** — Settings › ANONYMOUS DATA |
| Release **.aab** build script | **[DONE]** — `Assets/Editor/BuildRelease.cs` |
| Store listing copy | **[DONE]** — Part 6 below, ready to paste |
| Data safety answers | **[DONE]** — Part 7 below, ready to copy |
| Content rating answers | **[DONE]** — Part 8 below |
| Upload keystore | **[YOU]** — Part 2 |
| Google Play developer account ($25) | **[YOU]** — Part 1 |
| Screenshots + feature graphic | **[YOU]** — Part 5 |

---

## Part 1 — Developer account **[YOU]**

1. Go to <https://play.google.com/console> and sign in with the Google account
   you want to own this app **forever**. You cannot move an app between accounts
   later without a lot of pain — use an account you'll keep.
2. Pay the **one-off $25** registration fee.
3. Choose account type. As an individual you'll need to verify your identity
   with a government ID, and **your name and address become publicly visible**
   on your store listing. If you'd rather that not be public, register as an
   organisation instead — that needs a registered business and a D-U-N-S number.
4. Google now requires new personal developer accounts to run a **closed test
   with at least 12 testers for 14 continuous days** before you can go public.
   Start collecting those 12 email addresses now — this is usually the single
   longest delay in the whole process.

## Part 2 — The upload keystore **[YOU — I must not do this]**

This creates a password-protected signing key. I won't generate it or handle the
password: whoever holds it controls your app's identity, and **if you lose it you
can never update your app again.**

Run this in a terminal (Java comes with Unity, so `keytool` is already on your
machine — if `keytool` isn't found, use the one inside
`C:\Program Files\Unity\Hub\Editor\6000.4.11f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin`):

```bash
keytool -genkeypair -v -keystore trustissues.keystore -alias trustissues -keyalg RSA -keysize 2048 -validity 10000
```

It will ask you to invent a password and answer a few questions (name, city,
country — these are not shown to players).

**Then:**
- Store `trustissues.keystore` and its password somewhere you will still have in
  five years — a password manager, plus a second backup. Not just this laptop.
- **Do not commit it to git.** I've added it to `.gitignore` for you.
- In Unity: *Edit › Project Settings › Player › Publishing Settings* → tick
  **Custom Keystore**, browse to the file, enter the passwords, and pick the
  `trustissues` alias.

Also opt in to **Play App Signing** when the Console offers it (it's the
default). Google then holds the real signing key, and your keystore becomes just
an *upload* key — which means if you ever do lose it, support can reset it. This
is the safety net; take it.

## Part 3 — Host the privacy policy **[YOU]**

Play requires a **public URL** — a file on your computer won't do.

Easiest free option, and it uses the repo you already have:

1. Push the repo to GitHub (already done).
2. GitHub → your repo → **Settings › Pages** → Source: *Deploy from a branch*,
   branch `main`, folder `/docs`.
3. Wait ~2 minutes. Your policy is then live at:
   `https://shabbir-nuruddin.github.io/Game/PRIVACY_POLICY`
4. Open it and check it loads. That's the URL you paste into the Console.

## Part 4 — Build the release bundle

Play needs an **.aab** (app bundle), not the .apk you've been sideloading.

After you've set the keystore in Part 2, run:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.4.11f1\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod TrustIssues.EditorTools.BuildRelease.Build -logFile release.log
```

Output: `Builds/TrustIssues.aab`

Every update you upload must have a **higher version code** than the last. The
build script bumps it automatically and prints what it used.

## Part 5 — Assets you must supply **[YOU]**

| Asset | Requirement |
| --- | --- |
| App icon | 512 × 512 PNG, 32-bit, no transparency |
| Feature graphic | 1024 × 500 PNG/JPG — shown at the top of your listing |
| Phone screenshots | **At least 2**, ideally 8. 16:9 or 9:16, min 320px, max 3840px |
| Tablet screenshots | Only if you want tablet users to see a proper listing |
| Short description | 80 characters max |
| Full description | 4000 characters max |

Copy for the last two is written for you in Part 6.

For screenshots, the game already has a harness that grabs real frames:

```bash
./Builds/Win/TrustIssues.exe -shot C:\shots -floor 1 -warp 8 -touch -screen-width 1920 -screen-height 1080
```

## Part 6 — Store listing copy (paste this in)

**App name** (30 chars max):
```
Trust Issues
```

**Short description** (80 chars max):
```
A troll platformer that lies to you. The floor is not your friend.
```

**Full description:**
```
The castle wants you dead. It is also very funny about it.

Trust Issues is a rage platformer where the level itself is the enemy. Floors
collapse the moment you trust them. Spikes rise after you land. The bright,
obvious door is the one that kills you. Every trap is fair the second time —
and never the first.

Die and the castle tells you exactly what you did wrong, out loud, with no
sympathy whatsoever.

• 39 hand-built floors, each one a new lie
• Every floor is five short stages — death only sends you back to the stage
  you're on, so you always start again three seconds later
• A castle that LEARNS. Hesitate on a ledge and it builds something there
• Blood Moon nights, an Endless descent, and boss fights that each fight
  differently
• Race a friend live on the same track
• Cosmetics only. No pay-to-win, no ads, no in-app purchases

Built for thumbs. Playable in short bursts. Guaranteed to be someone else's
fault.
```

**Category:** Games › Arcade
**Tags:** platformer, hardcore, arcade
**Contact email:** nuruddinshabbir3@gmail.com
**Privacy policy URL:** the one from Part 3

## Part 7 — Data safety form (the exact answers)

This form is legally binding — it must match what the app really does. Based on
the actual code:

**Does your app collect or share any of the required user data types?** → **Yes**

Declare exactly these:

| Data type | Collected | Shared | Optional? | Purpose |
| --- | --- | --- | --- | --- |
| App interactions (gameplay events) | Yes | No | **Yes** — Settings toggle | Analytics |
| Other user-generated content (nickname) | Yes | Yes (to other players) | Yes | App functionality |

**Is all data encrypted in transit?** → **Yes** (the analytics endpoint and
Photon are both HTTPS/TLS)

**Do you provide a way to request data deletion?** → **Yes** — the email in the
privacy policy

**Do NOT tick:** location, personal info, financial info, health, photos, files,
contacts, calendar, messages, device IDs. The game collects none of them.

> If you later add ads or analytics SDKs, this form must be updated **before**
> that version ships.

## Part 8 — Content rating questionnaire

Answer honestly; these are the correct answers for this game:

- **Violence:** Yes — cartoon/fantasy. The player character dies in a stylised
  pixel-art way with red particle effects.
- **Blood:** Yes — stylised, non-realistic (pixel blood, "Blood Shards").
- **Sexual content / nudity:** No
- **Language:** Mild. The death lines are mocking but contain no profanity.
- **Controlled substances:** No
- **Gambling / simulated gambling:** No
- **User interaction:** **Yes** — players can race each other and see each
  other's nicknames.
- **Shares location:** No
- **Digital purchases:** No

Expected outcome: **PEGI 7 / ESRB Everyone 10+** or thereabouts.

## Part 9 — Release order (do it in this order)

1. Create the app in the Console: name, language, "Game", "Free".
2. Complete **App content**: privacy policy URL, ads declaration (**No ads**),
   content rating, target audience, data safety, government-app (No), financial
   features (None).
3. Upload the .aab to **Internal testing** first. Install it from the test link
   on a real phone and play it. Never send an untested build to closed testing.
4. Move to **Closed testing** and run the 12-tester / 14-day requirement.
5. Apply for **production** access.
6. Roll out to production — start at **20%** so you can halt it if crash reports
   spike.

## Part 10 — Before you press publish

- [ ] Keystore backed up in two places, password in a password manager
- [ ] Privacy policy URL loads publicly in a browser
- [ ] Version code higher than any previous upload
- [ ] The .aab installs and runs on a real phone from a test track
- [ ] Music attribution is visible in-game (Settings) — **this is a licence
      obligation for the Kevin MacLeod tracks, not a nicety**
- [ ] Data safety answers match Part 7
- [ ] Screenshots are of the real game, not mockups (Play rejects misleading
      store assets, and the mockup art is not what the game renders)

---

## Things that will get you rejected

- **Missing or unreachable privacy policy URL** — the single most common
  rejection.
- **Data safety form that doesn't match the app.** The game does send analytics;
  declaring "no data collected" would be a false declaration.
- **Screenshots that aren't the actual game.** Do not use the mockup paintings
  as screenshots.
- **Not targeting a recent Android API level.** Play raises this yearly; Unity
  6000.4 targets a current level by default, but check the Console warning.
- **Missing 64-bit support.** Already handled — ARM64 + IL2CPP.
