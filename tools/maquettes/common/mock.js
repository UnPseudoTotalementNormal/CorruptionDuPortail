// Mockup page plumbing: theme, screen scaling, ?only=<id> (one frame alone, for the captures), votes and
// remarks kept in localStorage and sent to /retours. Load it LAST, after the variants are built.
// The feedback goes to the page's folder; a send serve.py did not take shows its text to paste in the session.
// Everything stays inside this function: a page's own globals (fit, root...) are never replaced.
// From the "maquettes" template of the skill marketplace.
(() => {
  const root = document.documentElement;
  const pageTheme = root.dataset.theme || "dark";
  // the subject is the page's folder; data-subject only for a page outside one
  const subject = location.pathname.split("/").slice(-2, -1)[0] || document.body.dataset.subject || "divers";

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

  // Placing an object on a screen: <img class="place" data-name="bougie" data-x="380" data-y="760"> inside a .stage
  // (x, y in stage pixels, 1600 x 900 by default: the anchor, bottom centre unless the page's CSS says otherwise),
  // dragged with the finger or the mouse. data-scale="1" adds a size slider under its frame (data-scale-min / -max,
  // 0.5..1.5 by default). Kept per page and sent with the remarks ("E1 placement bougie : x=… y=… taille=…", plus the
  // element's data-info, which the page may fill). Each change fires "mockplace" on the element, bubbling, with
  // detail {variant, name, x, y, scale}: the page reacts (perspective size, the same object in another frame).
  const placeKey = "mock-place" + location.pathname;
  const placeSaved = only ? {} : JSON.parse(localStorage.getItem(placeKey) || "{}");
  const placeId = el => (el.closest(".variant") ? el.closest(".variant").dataset.id : "") + ":" + (el.dataset.name || "objet");
  function placeApply(el, x, y, scale, persist) {
    el.dataset.px = x; el.dataset.py = y; el.dataset.ps = scale;
    el.style.setProperty("--px", x); el.style.setProperty("--py", y); el.style.setProperty("--ps", scale);
    if (persist && !only) { placeSaved[placeId(el)] = { x, y, s: scale }; localStorage.setItem(placeKey, JSON.stringify(placeSaved)); }
    const variant = el.closest(".variant");
    el.dispatchEvent(new CustomEvent("mockplace", { bubbles: true, detail: { variant: variant ? variant.dataset.id : "", name: el.dataset.name || "objet", x, y, scale } }));
  }
  document.querySelectorAll(".stage .place").forEach(el => {
    const stage = el.closest(".stage"), screen = el.closest(".screen");
    const w = parseFloat(getComputedStyle(screen).getPropertyValue("--w")) || 1600;
    const h = parseFloat(getComputedStyle(screen).getPropertyValue("--h")) || 900;
    const st = placeSaved[placeId(el)];
    const def = { x: +el.dataset.x || w / 2, y: +el.dataset.y || h / 2, s: +(el.dataset.scale || 1) };
    el.draggable = false;
    let drag = null;
    el.addEventListener("pointerdown", e => {
      e.preventDefault(); el.setPointerCapture(e.pointerId); el.classList.add("dragging");
      drag = { sx: e.clientX, sy: e.clientY, x: +el.dataset.px, y: +el.dataset.py };
    });
    el.addEventListener("pointermove", e => {
      if (!drag) return;
      const k = stage.getBoundingClientRect().width / w;
      const x = Math.max(0, Math.min(w, drag.x + (e.clientX - drag.sx) / k));
      const y = Math.max(0, Math.min(h, drag.y + (e.clientY - drag.sy) / k));
      placeApply(el, Math.round(x), Math.round(y), +el.dataset.ps, false);
    });
    const end = () => { if (!drag) return; drag = null; el.classList.remove("dragging"); placeApply(el, +el.dataset.px, +el.dataset.py, +el.dataset.ps, true); };
    el.addEventListener("pointerup", end);
    el.addEventListener("pointercancel", end);
    if (el.dataset.scale !== undefined && !only) {
      const row = document.createElement("div"); row.className = "place-row";
      const min = +(el.dataset.scaleMin || 0.5), max = +(el.dataset.scaleMax || 1.5);
      row.innerHTML = '<span>Taille (' + (el.dataset.name || "objet") + ')</span><input type="range" min="' + min + '" max="' + max + '" step="0.01"><button type="button">Remettre</button>';
      screen.after(row);
      const range = row.querySelector("input");
      range.value = st ? st.s : def.s;
      range.oninput = () => placeApply(el, +el.dataset.px, +el.dataset.py, +range.value, true);
      row.querySelector("button").onclick = () => { range.value = def.s; placeApply(el, def.x, def.y, def.s, true); };
    }
    placeApply(el, st ? st.x : def.x, st ? st.y : def.y, st ? st.s : def.s, false);
  });

  // A field opens the keyboard only when the finger really lands on it. On a phone Chromium hands a tap that falls
  // near an editable field (a scroll too short to count, a touch on the overlay scroll bar under a remark) to that
  // field; pointerdown is hit-tested at the raw point, so every other field is read-only until the next touch.
  const typed = "textarea, input:not([type]), input[type=text], input[type=search], input[type=email], input[type=url], input[type=number]";
  let locked = [];
  addEventListener("pointerdown", e => {
    locked.forEach(f => f.readOnly = false);
    if (e.pointerType !== "touch") return void (locked = []);
    locked = [...document.querySelectorAll(typed)].filter(f => !f.readOnly && f !== e.target && f !== document.activeElement &&
      ![...(f.labels || [])].some(l => l.contains(e.target)));
    locked.forEach(f => f.readOnly = true);
  }, true);

  // theme: <div class="toggle"><button data-t="dark">Sombre</button><button data-t="light">Clair</button></div>
  function setTheme(t) {
    root.dataset.theme = t;
    localStorage.setItem("mock-theme", t);
    document.querySelectorAll(".toggle button").forEach(b => b.classList.toggle("on", b.dataset.t === t));
  }
  document.querySelectorAll(".toggle button").forEach(b => b.onclick = () => setTheme(b.dataset.t));
  setTheme(localStorage.getItem("mock-theme") || pageTheme);

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
      document.querySelectorAll(".stage .place").forEach(el => {
        const variant = el.closest(".variant");
        if (!placeSaved[placeId(el)]) return; // never moved: nothing chosen
        lines.push((variant ? variant.dataset.id + " " : "") + "placement " + (el.dataset.name || "objet") + " : x=" + el.dataset.px +
          " y=" + el.dataset.py + " taille=" + (+el.dataset.ps).toFixed(2) + (el.dataset.info ? " (" + el.dataset.info + ")" : ""));
      });
      if ((saved._global || "").trim()) lines.push("Général : " + saved._global.trim());
      if (!lines.length) { send.textContent = "Rien à envoyer pour l’instant"; setTimeout(() => send.textContent = label, 2000); return; }
      lines.unshift("Page : " + location.pathname);
      if (document.querySelector(".toggle") && root.dataset.theme !== pageTheme) lines.push("Thème regardé : " + root.dataset.theme);
      const text = lines.join("\n");
      let got = false;
      try { // only serve.py answers {"ok":true}: a page opened as a file, or by another server (static, dev server), keeps nothing
        const r = await fetch("/retours?s=" + encodeURIComponent(subject) + "&t=" + encodeURIComponent(text));
        got = r.ok && (await r.json()).ok === true;
      } catch (e) {}
      let copy = document.getElementById("send-copy");
      if (got) {
        if (copy) copy.remove();
        send.textContent = "Envoyé ✓, dis-le à Claude dans la session";
        return;
      }
      // not received: the whole text, to paste in the session instead of retyping every choice
      if (!copy) {
        copy = document.createElement("textarea");
        copy.id = "send-copy";
        copy.readOnly = true;
        send.after(copy);
      }
      copy.value = text;
      copy.rows = Math.min(lines.length + 1, 14);
      copy.select();
      let copied = false;
      try { await navigator.clipboard.writeText(text); copied = true; } catch (e) {}
      send.textContent = copied ? "Claude ne l’a pas reçu : texte copié, colle-le dans la session"
                                : "Claude ne l’a pas reçu : copie ce texte dans la session";
    };
  }
})();
