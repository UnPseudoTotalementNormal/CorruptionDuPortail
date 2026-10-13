// Mockup page plumbing: screen scaling, ?only=<id> (one screen alone, for the captures), votes + remarks saved in
// localStorage and sent to /retours. Load LAST, after the variants are built. The page sets <body data-subject>.
const subject = document.body.dataset.subject || "divers";

const only = new URLSearchParams(location.search).get("only");
if (only) {
  const s = document.querySelector('.variant[data-id="' + only + '"] .screen');
  [...document.body.children].forEach(c => { if (c.tagName.toLowerCase() !== "script") c.remove(); });
  document.body.style.cssText = "margin:0;background:#000";
  document.body.appendChild(s);
}

function fit() {
  document.querySelectorAll(".screen").forEach(s => s.style.setProperty("--k", s.clientWidth / 1600));
}
addEventListener("resize", fit);
fit();

if (!only) {
  const key = "cdp-mock" + location.pathname;
  const saved = JSON.parse(localStorage.getItem(key) || "{}");
  const save = () => localStorage.setItem(key, JSON.stringify(saved));
  document.querySelectorAll(".variant").forEach(v => {
    const id = v.dataset.id;
    const st = saved[id] || (saved[id] = { vote: "", note: "" });
    const box = document.createElement("div");
    box.innerHTML = '<div class="vote"><button class="like">👍 J’aime</button><button class="nope">👎 Non</button></div>' +
      '<textarea class="note" placeholder="Une remarque sur ' + id + ' ?"></textarea>';
    v.appendChild(box);
    const like = box.querySelector(".like"), nope = box.querySelector(".nope"), note = box.querySelector("textarea");
    const paint = () => { like.classList.toggle("on", st.vote === "like"); nope.classList.toggle("on", st.vote === "nope"); };
    like.onclick = () => { st.vote = st.vote === "like" ? "" : "like"; paint(); save(); };
    nope.onclick = () => { st.vote = st.vote === "nope" ? "" : "nope"; paint(); save(); };
    note.value = st.note; note.oninput = () => { st.note = note.value; save(); };
    paint();
  });
  const global = document.getElementById("global");
  if (global) { global.value = saved._global || ""; global.oninput = () => { saved._global = global.value; save(); }; }

  const send = document.getElementById("send");
  send.onclick = async () => {
    const lines = [];
    document.querySelectorAll(".variant").forEach(v => {
      const st = saved[v.dataset.id];
      if (!st || (!st.vote && !st.note.trim())) return;
      const name = v.querySelector(".vname").textContent;
      lines.push(v.dataset.id + " (" + name + ") : " + (st.vote === "like" ? "👍" : st.vote === "nope" ? "👎" : "–") + (st.note.trim() ? " " + st.note.trim() : ""));
    });
    if ((saved._global || "").trim()) lines.push("Général : " + saved._global.trim());
    if (!lines.length) { send.textContent = "Rien à envoyer pour l’instant"; setTimeout(() => send.textContent = "Envoyer mes retours à Claude", 2000); return; }
    lines.unshift("Page : " + location.pathname);
    try {
      const r = await fetch("/retours?s=" + encodeURIComponent(subject) + "&t=" + encodeURIComponent(lines.join("\n")));
      send.textContent = r.ok ? "Envoyé ✓, dis-le à Claude dans la session" : "Échec de l’envoi";
    } catch (e) { send.textContent = "Échec de l’envoi"; }
  };
}
