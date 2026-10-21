// Corruption du Portail — playtest bug reports (F8 window) and crash alerts → Discord « rapports-de-bug ».
// A playtest build writes reports/<id> (Firestore) and reports/<id>.jpg (Storage); this function posts the report with
// its capture and the tools/logs/logs.py command that prints the moment, so Claude can read it at once.
// Deploy: firebase deploy --only functions --project corruption-du-portail (from firebase/). The bot token lives in
// Secret Manager (firebase functions:secrets:set DISCORD_BOT_TOKEN), never in a client.

const { onDocumentCreated } = require("firebase-functions/v2/firestore");
const { defineSecret, defineString } = require("firebase-functions/params");
const logger = require("firebase-functions/logger");
const { initializeApp } = require("firebase-admin/app");
const { getStorage } = require("firebase-admin/storage");
const { getFirestore, FieldValue, Timestamp } = require("firebase-admin/firestore");

initializeApp();

const botToken = defineSecret("DISCORD_BOT_TOKEN");
const channelId = defineString("DISCORD_REPORTS_CHANNEL_ID");

const { buildMessage } = require("./message");

// Posted at most once: the document records the post (postedAt). Retries are on (a Discord 429/5xx or a timeout is
// tried again later); a claim (postingSince) keeps two deliveries of the same event from posting together.
const CLAIM_MS = 2 * 60 * 1000;

exports.postReportToDiscord = onDocumentCreated(
  { document: "reports/{id}", region: "europe-west1", secrets: [botToken], retry: true, memory: "256MiB" },
  async (event) => {
    const ref = event.data && event.data.ref;
    const report = event.data && event.data.data();
    // tools/logs/check_rules.py writes reports as player "RulesCheck": never posted.
    if (!ref || !report || report.player === "RulesCheck") return;
    if (Date.now() - new Date(event.time).getTime() > 24 * 3600 * 1000) return; // retried for a day at most

    const claimed = await getFirestore().runTransaction(async (tx) => {
      const snap = await tx.get(ref);
      const doc = snap.data();
      if (!doc || doc.postedAt) return false;
      if (doc.postingSince && Date.now() - doc.postingSince.toMillis() < CLAIM_MS) return false;
      tx.update(ref, { postingSince: Timestamp.now() });
      return true;
    });
    if (!claimed) return;

    const payload = buildMessage(report);
    const form = new FormData();
    form.append("payload_json", JSON.stringify(payload));
    if (report.capture) {
      try {
        const [bytes] = await getStorage().bucket().file(report.capture).download();
        form.append("files[0]", new Blob([bytes], { type: "image/jpeg" }), "capture.jpg");
      } catch (error) {
        logger.warn("capture missing, posting without it", { id: event.params.id, error: String(error) });
        delete payload.embeds[0].image;
        form.set("payload_json", JSON.stringify(payload));
      }
    }
    const response = await fetch(`https://discord.com/api/v10/channels/${channelId.value()}/messages`, {
      method: "POST",
      headers: { Authorization: `Bot ${botToken.value()}` },
      body: form,
    });
    if (!response.ok) {
      const body = await response.text();
      logger.error("Discord refused the report", { id: event.params.id, status: response.status, body: body.slice(0, 500) });
      if (response.status === 429 || response.status >= 500) {
        await ref.update({ postingSince: FieldValue.delete() });
        throw new Error(`Discord ${response.status}: retried`);
      }
      await ref.update({ postedAt: Timestamp.now(), postError: `Discord ${response.status}` }); // will never pass
      return;
    }
    await ref.update({ postedAt: Timestamp.now() });
    logger.info("report posted", { id: event.params.id, kind: report.kind });
  },
);
