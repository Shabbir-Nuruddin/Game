# Publishing Trust Issues on Google Play

**Reviewed against the project and Google Play requirements on 6 August 2026.**

Google Play rules change. Recheck the linked official pages immediately before
submitting a release.

## Current readiness

| Item | Current state |
| --- | --- |
| Package name `com.shabbir.trustissues` | Ready |
| Android ARM64 + IL2CPP | Configured |
| Android App Bundle build | `Assets/Editor/BuildRelease.cs` exists |
| Privacy Policy | Updated in `docs/PRIVACY_POLICY.md`; must be hosted and linked inside the app |
| Terms of Use | Drafted in `docs/TERMS_OF_USE.md`; online-feature acceptance is not implemented yet |
| Data safety draft | Updated below |
| Content rating draft | Updated below |
| Analytics opt-out | Exists, but affects analytics only—not leaderboards or death echoes |
| Ads | No ads are shown; unused Unity Ads package is still bundled |
| In-app purchases | None; unused Unity Purchasing package is still installed |
| Target API | Set to Unity “Automatic”; verify the generated bundle targets API 36 for submissions on/after 31 August 2026 |
| Upload keystore | Developer must create and protect it |
| Public store assets | Still required |
| Closed test | May require 12 continuously opted-in testers for 14 days |

## Important blockers before production

### 1. Put Privacy and Terms links inside the Game

Google requires the Privacy Policy to be accessible inside the app as well as
in Play Console. Add visible **PRIVACY** and **TERMS** buttons to Settings.

After GitHub Pages is enabled for the `/docs` folder, use:

- `https://shabbir-nuruddin.github.io/Game/PRIVACY_POLICY.html`
- `https://shabbir-nuruddin.github.io/Game/TERMS_OF_USE.html`

Open both URLs in a private browser window before submitting. The pages must be
public, active, non-geofenced, non-editable by visitors, and not PDF files.

### 2. Fix the nickname/UGC compliance path

Players can type nicknames that appear in multiplayer, leaderboards, and death
echoes. Google treats visible player submissions as user-generated content
(UGC). A Terms page and an email address alone are not enough for a public UGC
feature.

Choose one route before production:

**Simplest route:** remove free-text nicknames and offer only generated or
pre-approved names such as `Heir-412`. This substantially reduces UGC risk.

**Full UGC route:** before the player uses online features:

- show the Terms/behavior rules and require non-skippable acceptance;
- add clearly labelled in-app **Report** and **Block** controls;
- maintain a moderation process and act on reports promptly; and
- allow removal of reported nicknames/content from the server.

### 3. Make online sharing optional or disclose it prominently

The **ANONYMOUS DATA** switch stops the custom analytics queue, but solo deaths
can still send a nickname, install ID, death location, and cause to the death-
echo service. Leaderboard results are also separate.

Before release, add a separate **ONLINE COMMUNITY FEATURES** choice or an
up-front disclosure. The cleanest implementation is opt-in and should control
leaderboards, death echoes, and public nicknames together.

### 4. Remove unused SDKs

The project currently includes Unity Ads, Unity Analytics, and Unity Purchasing
packages even though their services are disabled and the Game does not use
them. Remove unused packages before the release bundle. This reduces app size,
SDK-policy risk, and the chance that the Data safety declaration becomes wrong
after a package update.

### 5. Establish a fixed retention process

The old policy claimed analytics automatically expired after 24 months, but the
server does not currently enforce that. The updated policy states the current
behavior honestly. Before launch, adopt and automate a concrete retention rule
(recommended: delete raw analytics and death echoes after 24 months) and then
update the policy to match.

## 1. Developer account and verification

1. Create or use a Play Console account at <https://play.google.com/console>.
2. Select the correct account type:
   - **Personal** for an individual/hobby developer.
   - **Organization** for a registered business; a D-U-N-S number is required.
3. Complete identity, payment-profile, contact-email, and contact-phone
   verification requested by Play Console.
4. New personal accounts must verify access to a non-rooted physical Android
   phone (Android 10 or newer) through the Play Console mobile app.
5. Enable 2-Step Verification and never share the account password.

Play Console tells you exactly which verified developer details will be public.
Personal and organization accounts have different display requirements.

## 2. Testing requirement for new personal accounts

If the personal developer account was created after 13 November 2023, production
access normally requires:

- a closed test;
- at least **12 testers**;
- all 12 remaining opted in continuously for at least **14 days**; and
- a production-access application explaining testing, feedback, and fixes.

Start with internal testing, then begin the closed test only when the build is
stable enough that testers can keep it installed for the full period.

## 3. Signing and release bundle

New Play apps publish with an Android App Bundle (`.aab`), not the sideload APK.

1. Create an RSA upload key of at least 2048 bits and store it outside Git.
2. Back up the keystore and passwords in at least two secure locations.
3. Configure the keystore in Unity Player Publishing Settings.
4. Enrol in **Play App Signing**. Keep the upload key; Google protects the app
   signing key.
5. Build `Builds/TrustIssues.aab` with the release builder.
6. Confirm each upload has a higher version code than every previous upload.

Example key creation command (run it yourself; never paste the password into a
chat or commit it):

```powershell
keytool -genkeypair -v -keystore trustissues.keystore -alias trustissues -keyalg RSA -keysize 2048 -validity 10000
```

## 4. Android technical requirements

- Use the `.aab` release build, ARM64, and IL2CPP.
- Target **Android 15 / API 35 or higher** for submissions before 31 August
  2026.
- Starting **31 August 2026**, new mobile apps and updates must target **Android
  16 / API 36 or higher**. Target API 36 now if the installed Unity Android tools
  support it.
- Keep the minimum SDK only as high as genuinely needed (currently API 26).
- Inspect the final merged Android manifest for unnecessary permissions and SDK
  declarations after building.
- Test the Play-delivered build through Internal App Sharing or an internal test
  track—not only a locally installed APK.
- Verify multiplayer on two different physical phones/networks.

## 5. Required store listing material

Prepare:

- app name;
- short description (maximum 80 characters);
- full description (maximum 4,000 characters);
- 512 × 512 store icon;
- 1024 × 500 feature graphic;
- at least two real phone screenshots (more are recommended);
- category, tags, support email, and optional support website; and
- countries/regions, pricing, and distribution selections.

Use screenshots from the real current mobile build. Do not present concept art
as gameplay.

### Suggested listing copy

**Name:** Trust Issues

**Short description:**

> A troll platformer where every floor lies—and every death teaches the trick.

**Full description:**

> The castle wants you dead. Worse, it wants you confident first.
>
> Trust Issues is a trap-filled platformer where floors collapse, gates betray
> you, spikes wait for the perfect moment, and the obvious route is usually the
> wrong one. Every trick is surprising the first time and readable the next.
>
> • 40 hand-built Castle floors
>
> • Fast retries and stage checkpoints
>
> • Blood Moon challenges and an Endless distance run
>
> • Boss fights, moving traps, collapsing floors, and cursed rooms
>
> • Optional live races with friends
>
> • Leaderboards, death echoes, cosmetics, and custom trap challenges
>
> • No advertisements and no in-app purchases
>
> Built for short sessions, stubborn players, and the friend who said, “That
> jump looks safe.”

Suggested category: **Games → Arcade**.

## 6. Play Console “App content” declarations

Complete every card shown in Play Console, including:

- Privacy Policy URL;
- Data safety;
- Ads declaration: **No, the app does not contain ads** (only after confirming
  no ad is displayed in the release build);
- App access: generally **all functionality is available without special
  access**; include concise instructions for creating/joining a multiplayer room
  if reviewers need them;
- Target audience and content;
- IARC content-rating questionnaire;
- Government app: **No**;
- News app: **No**;
- Financial features: **None**;
- Health features: **None**;
- any permissions declaration Play Console generates from the uploaded bundle;
  and
- any additional declarations that Play Console adds before submission.

## 7. Data safety draft for the current build

This is a conservative draft based on the current source. Re-check the final
`.aab` and every bundled SDK before submitting.

**Does the app collect or share user data?** **Yes**

| Google Play data type | Collected | Shared | Required/optional now | Purpose |
| --- | --- | --- | --- | --- |
| Personal info → Name (nickname) | Yes | Yes, visible to players | Required by the current death-echo implementation | App functionality |
| App activity → App interactions | Yes | No | Optional through ANONYMOUS DATA | Analytics |
| App activity → Other actions (gameplay, deaths, scores) | Yes | Some results are visible to players | Required for current online features; analytics portion optional | App functionality, analytics |
| Device or other IDs → random installation ID | Yes | No public sharing; processors receive it | Required by current death echoes; analytics portion optional | App functionality, analytics |

Also answer:

- **Encrypted in transit:** Yes, assuming the final Photon and HTTPS
  configuration remains in place.
- **Users can request deletion:** Yes, through the privacy contact email.
- **Account creation:** No. Therefore Play's account-deletion URL requirement
  does not currently apply.
- **Data sold:** No.

Do not declare location, contacts, messages, camera, microphone recordings,
photos, health, financial information, purchase history, or advertising data
unless the final bundle or a later feature actually collects them.

## 8. Target audience and children

Recommended target groups for the current design:

- **13–15**
- **16–17**
- **18 and over**

Do not select an under-13 group unless you deliberately redesign for Google
Play Families requirements. The current build has online nicknames, analytics,
Photon multiplayer, public death echoes, and SDKs that have not been audited for
child-directed use.

If you want to officially target 8–12-year-olds, that is a separate compliance
project: use a neutral age screen or child-safe mode, stop disallowed identifier
transmission for children, audit every SDK for Families eligibility, restrict
online interactions, and obtain any legally required parental consent.

## 9. Content-rating questionnaire

Answer from the actual final build, not from the desired rating:

- fantasy/cartoon violence: **Yes**;
- stylised blood or gore particles: **Yes**;
- horror/fear themes, vampires, coffins, and death: **Yes**;
- strong language: review every authored line; the milestone story is mild, but
  free-text nicknames mean user content can vary;
- sexual content/nudity: **No**;
- controlled substances: **No**;
- gambling/simulated gambling: **No**;
- digital purchases: **No**;
- users interact or exchange visible information: **Yes**;
- user-generated content: **Yes** while free-text nicknames remain;
- location sharing: **No**.

Do not promise a specific PEGI/ESRB result. IARC assigns regional ratings after
the questionnaire.

## 10. Privacy Policy, Terms, and licences

A custom end-user licence is not usually a separate Play Console upload for a
simple game. However:

- the Privacy Policy is mandatory;
- Terms of Use are effectively required here because the Game displays player
  nicknames/UGC;
- the Game must obtain acceptance of UGC rules before online participation;
- third-party software and asset licences must be retained; and
- Kevin MacLeod music attribution must stay visible and accurate under the
  applicable Creative Commons licence.

Keep proof of licences for every music track, sound, font, image, Unity asset,
and third-party code component. Google may request evidence following an IP
complaint.

## 11. Release sequence

1. Resolve the blockers at the top of this document.
2. Remove unused SDKs and build the signed `.aab`.
3. Create the app in Play Console and complete developer verification.
4. Upload to internal testing.
5. Complete the store listing and all App content declarations.
6. Install the Play-delivered build on multiple real phones.
7. Test offline startup, gameplay, settings, privacy links, data opt-outs,
   sharing, TTS, two-device Photon multiplayer, and deletion/report workflows.
8. Run the required closed test and retain tester feedback.
9. Apply for production access if required.
10. Submit production and monitor Android vitals, crashes, reviews, and policy
    notices.

## 12. Final checklist

- [ ] Developer identity, email, phone, payment profile, and device verified
- [ ] 2-Step Verification enabled
- [ ] Package name final and registered
- [ ] Upload keystore backed up securely
- [ ] Play App Signing enabled
- [ ] Release `.aab` targets the required API level
- [ ] Version code is unique and higher
- [ ] Unused Ads/Analytics/Purchasing SDKs removed
- [ ] Final permissions and SDK behavior audited
- [ ] Privacy Policy hosted publicly and linked in Settings
- [ ] Terms hosted and accepted before online/UGC use
- [ ] Report and Block implemented, or free-text nicknames removed
- [ ] Online community-data choice/disclosure implemented
- [ ] Data retention process decided and enforced
- [ ] Data safety answers match the final bundle
- [ ] Content rating and target audience are accurate
- [ ] Store icon, feature graphic, and real screenshots uploaded
- [ ] Support email works and is monitored
- [ ] Music/asset/software licence evidence retained
- [ ] Internal and closed testing completed as required
- [ ] Multiplayer tested on two physical devices

## Official references

- User Data and Privacy Policy requirements:
  <https://support.google.com/googleplay/android-developer/answer/10144311>
- Data safety:
  <https://support.google.com/googleplay/android-developer/answer/10787469>
- UGC moderation:
  <https://support.google.com/googleplay/android-developer/answer/12923286>
- Target API levels:
  <https://support.google.com/googleplay/android-developer/answer/11926878>
- New personal-account testing:
  <https://support.google.com/googleplay/android-developer/answer/14151465>
- Developer verification:
  <https://support.google.com/googleplay/android-developer/answer/10841920>
- Play App Signing:
  <https://support.google.com/googleplay/android-developer/answer/9842756>
- Content ratings:
  <https://support.google.com/googleplay/android-developer/answer/9859655>
