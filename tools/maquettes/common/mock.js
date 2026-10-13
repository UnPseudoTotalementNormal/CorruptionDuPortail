// Mockup page plumbing: theme, screen scaling, ?only=<id> (one frame alone, for the captures), votes and
// remarks kept in localStorage and sent to /retours. Load it LAST, after the variants are built.
// The page sets <body data-subject="<folder>">. Everything stays inside this function: a page's own globals
// (fit, root...) are never replaced. From the "maquettes" template of the skill marketplace.
(() => {
  const root = document.documentElement;
  const subject = document.body.dataset.subject || "divers";

  // ?only=E1 : that variant's frame alone (.phone or .screen; a variant without a frame, whole)
  const only = new URLSearchParams(location.search).get("only");
  if (only) {
    const variant = document.querySelector('.variant[data-id="' + only + '"]');
    const frame = variant.querySelector(".phone, .screen") || variant;
    [...document.body.children].forEach(c => { if (!["script", "svg"].includes(c.tagName.toLowerCase())) c.remove(); });
    // a phone keeps its size with a margin around it; a screen takes the whole window (capture at its ratio)
    document.body.style.cssText = frame.classList.contains("phone") ? "margin:0;padding:10px;display:inline-block" : "margin:0";
    document.body.appendChild(frame);
  }

  // .screen: drawn at its design size (.stage, --w x --h, 1600 x 900 by default) and scaled to its width
  function fit() {
    document.querySelectorAll(".screen").forEach(s => {
      const w = parseFloat(getComputedStyle(s).getPropertyValue("--w")) || 1600;
      s.style.setProperty("--k", s.clientWidth / w);
    });
  }
  addEventListener("resize", fit);
  fit();

  // theme: <div class="toggle"><button data-t="dark">Sombre</button><button data-t="light">Clair</button></div>
  function setTheme(t) {
    root.dataset.theme = t;
    localStorage.setItem("mock-theme", t);
    document.querySelectorAll(".toggle button").forEach(b => b.classList.toggle("on", b.dataset.t === t));
  }
  document.querySelectorAll(".toggle button").forEach(b => b.onclick = () => setTheme(b.dataset.t));
  setTheme(localStorage.getItem("mock-theme") || root.dataset.theme || "dark");

  if (!only) {
    const key = "mock" + location.pathname;
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
    const label = send.textContent;
    send.onclick = async () => {
      const lines = [];
      document.querySelectorAll(".variant").forEach(v => {
        const st = saved[v.dataset.id];
        if (!st || (!st.vote && !st.note.trim())) return;
        const name = v.querySelector(".vname").textContent;
        lines.push(v.dataset.id + " (" + name + ") : " + (st.vote === "like" ? "👍" : st.vote === "nope" ? "👎" : "–") + (st.note.trim() ? " " + st.note.trim() : ""));
      });
      if ((saved._global || "").trim()) lines.push("Général : " + saved._global.trim());
      if (!lines.length) { send.textContent = "Rien à envoyer pour l’instant"; setTimeout(() => send.textContent = label, 2000); return; }
      lines.unshift("Page : " + location.pathname);
      if (document.querySelector(".toggle")) lines.push("Thème regardé : " + root.dataset.theme);
      try {
        const r = await fetch("/retours?s=" + encodeURIComponent(subject) + "&t=" + encodeURIComponent(lines.join("\n")));
        send.textContent = r.ok ? "Envoyé ✓, dis-le à Claude dans la session" : "Échec de l’envoi";
      } catch (e) { send.textContent = "Échec de l’envoi"; }
    };
  }
})();
