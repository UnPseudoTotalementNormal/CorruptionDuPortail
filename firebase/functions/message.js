// The Discord message of a report document (pure, no Firebase): used by index.js, checked by test.js.

const PARIS = "Europe/Paris";

function parisTime(date) {
  return new Intl.DateTimeFormat("fr-FR", {
    timeZone: PARIS, hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
  }).format(date);
}

function parisDate(date) {
  return new Intl.DateTimeFormat("fr-FR", { timeZone: PARIS, day: "2-digit", month: "2-digit" }).format(date);
}

// Exact id formats (the same as the Firestore rules): the command line is only built from ids that match them.
const SESSION_ID = /^[0-9]{8}-[0-9]{6}-[0-9a-f]{6}$/;
const GAME_ID = /^[0-9]{8}-[0-9]{4}-[0-9a-f]{4}$/;

/** Plain text in an embed: no markdown, no masked link, no mention can come out of a player's words. */
function plain(text) {
  return String(text == null ? "" : text).replace(/([\\*_~`|>\[\]()<#@:-])/g, "\\$1");
}

function clip(text, max) {
  return text.length > max ? text.slice(0, max - 1) + "…" : text;
}

/** The Discord message for one report document (pure: tested by test.js). */
function buildMessage(report) {
  const when = report.t && typeof report.t.toDate === "function" ? report.t.toDate() : new Date(report.t || Date.now());
  const hms = parisTime(when);
  const crash = report.kind === "crash";
  const command = GAME_ID.test(report.gameId || "")
    ? `python tools/logs/logs.py around ${report.gameId} ${hms} --window 90`
    : SESSION_ID.test(report.sid || "")
      ? `python tools/logs/logs.py timeline ${report.sid} --level W`
      : "(identifiants invalides : pas de commande)";
  const where = [plain(report.phase), report.day ? `jour ${Number(report.day) || 0}` : null, plain(report.role),
    report.seat ? `place ${plain(report.seat)}` : null]
    .filter(Boolean).join(", ") || "hors partie";
  const fields = [
    { name: "Joueur", value: clip(plain(report.player) || "?", 100), inline: true },
    { name: crash ? "Dernière ligne" : "Quand", value: `${parisDate(when)} ${hms}`, inline: true },
    { name: "Build", value: clip(`${plain(report.version) || "?"} ${plain(report.flavor)}`.trim(), 100), inline: true },
    { name: "Moment", value: clip(where, 200), inline: false },
    { name: "Lire les logs", value: "```" + command + "```", inline: false },
  ];
  const embed = {
    title: crash ? "Crash" : "Signalement",
    color: crash ? 15548997 : 15105570,
    description: crash
      ? "Le jeu s'est fermé sans finir sa session (vu au lancement suivant)."
      : clip(report.text && report.text.trim() ? plain(report.text) : "(sans texte)", 3800),
    fields,
    footer: { text: clip(`session ${plain(report.sid) || "?"}${report.gameId ? ", partie " + plain(report.gameId) : ""}`, 200) },
  };
  if (report.capture) {
    embed.image = { url: "attachment://capture.jpg" };
  }
  return { embeds: [embed], allowed_mentions: { parse: [] } };
}

module.exports = { buildMessage };
