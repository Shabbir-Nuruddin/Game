# Making your upload keystore

A keystore is a single small file plus two passwords. It is how Google knows a
future update to Trust Issues really came from you. **If you lose it, you can
never update this app again** — you would have to publish a brand new listing and
leave your existing players behind. So the important part of this guide is not
making it. It is keeping it.

You only ever do this once.

---

## Step 1 — Make the file

Unity can build it for you, so you never touch a command line.

1. Open the project in Unity.
2. Menu bar: **File → Build Profiles** (older Unity calls this *Build Settings*).
3. Pick **Android** on the left, then click **Player Settings…**
4. In the panel that opens, expand **Publishing Settings**.
5. Click **Keystore Manager… → Keystore… → Create New → Anywhere…**
6. Save it somewhere that is **not** inside the project folder. Your Documents
   folder is fine. Name it `trust-issues-upload.keystore`.

Unity then asks for the details:

| Field | What to put |
|---|---|
| Password / Confirm password | A password you invent. Write it down now. |
| Alias | `upload` |
| Alias password | Use the same password as above — one less thing to lose. |
| Validity (years) | `50` — Google requires the key to outlive 22 October 2033. |
| First and Last Name | Your name |
| Organization | Leave blank, or your studio name if you have one |
| City / State / Country Code | Your city, your state, and a two-letter country code (`IN`, `US`, `GB`…) |

Click **Add Key**. Unity asks if you want to use it for this project — say **yes**.

---

## Step 2 — Back it up before you do anything else

Do this today, not "later". You need **three** copies in **different places**:

1. The original where you saved it.
2. A cloud drive (Google Drive, OneDrive, Dropbox).
3. A USB stick, an external drive, or a second computer.

Store the two passwords in a password manager, or written on paper kept with the
USB stick. Do **not** put the passwords in the same folder as the keystore file
in a plain text file, and do **not** commit the keystore to GitHub — this project
is a public repo, and a leaked keystore means anyone can sign fake updates.

> Sanity check: if your laptop were stolen tonight, could you still publish an
> update tomorrow? If the answer is no, you have not finished this step.

---

## Step 3 — Turn on Play App Signing

When you upload your first release, the Play Console offers **Play App Signing**.
**Turn it on.** Google then holds the real signing key, and your keystore becomes
just the *upload* key. If you ever lose the upload key, Google support can reset
it — which turns a fatal mistake into a support ticket. There is no downside.

---

## Step 4 — Build the file you actually upload

The Play Store does **not** take the `.apk` you have been sideloading. It wants an
**Android App Bundle** (`.aab`).

In the same Build Profiles window:

1. Tick **Build App Bundle (Google Play)**.
2. Confirm **Publishing Settings** shows your keystore and the `upload` alias,
   with both passwords filled in.
3. Click **Build**, and save as `TrustIssues.aab`.

That `.aab` is what you drag into the Play Console.

> Unity forgets keystore passwords when you close it. If a build suddenly
> complains about signing, re-enter them under Publishing Settings — the file
> itself is fine.

---

## Things that will bite you

- **The package name is permanent.** Ours is `com.shabbir.trustissues`. Once it is
  live on Play you can never change it — a new name is a new app with zero
  installs and zero reviews.
- **Version code must go up every upload.** Play rejects a build whose version code
  it has seen. Our build script already bumps it automatically.
- **A debug-signed APK will be rejected.** The sideload APK you are testing with is
  signed with Unity's throwaway debug key on purpose, so it can never be mistaken
  for a shippable build.

---

## What "developer name" means for your privacy

Worth knowing before you register: the Play Store shows a **developer name** on
every listing, publicly. A privacy policy also has to identify who publishes the
app — that is the whole point of the document, so it cannot be anonymous.

If you would rather your personal name not be the public face of this, the time to
decide is **when you create the Play Console account**, not after:

- A **personal** developer account publishes under your legal name, and since 2023
  Google also requires a verified physical address to be shown for personal
  accounts that distribute to consumers.
- An **organisation** account publishes under a business name instead. It needs a
  D-U-N-S number, which is free but takes a couple of weeks.

Either way the name in the policy and the name on the listing should match. Tell
me which route you pick and I will update both documents to suit.
