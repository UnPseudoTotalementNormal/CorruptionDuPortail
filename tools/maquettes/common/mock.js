// Mockup page plumbing: theme, screen scaling, ?only=<id> (one frame alone, for the captures), sections that follow
// the layout section, pinch and « En entier » on a phone, votes and remarks kept in localStorage and sent to /retours. Load it LAST, after the variants are built.
// The feedback goes to the page's folder; a send serve.py did not take shows its text to paste in the session.
// Everything stays inside this function: a page's own globals (fit, root...) are never replaced.
// From the "maquettes" template of the skill marketplace.
(() => {
  const root = document.documentElement;
  const pageTheme = root.dataset.theme || "dark";
  // the subject is the page's folder; data-subject only for a page outside one
  const subject = location.pathname.split("/").slice(-2, -1)[0] || document.body.dataset.subject || "divers";

  const params = new URLSearchParams(location.search);
  const only = params.get("only");
  const key = "mock" + location.pathname;
  const saved = only ? {} : JSON.parse(localStorage.getItem(key) || "{}");
  const save = () => localStorage.setItem(key, JSON.stringify(saved));

  // The section that decides the layout follows his vote.
  // <section data-forme="P1 P2 P3"> : the layout section (the ids a frame can be drawn in; empty: all its variants).
  // <section data-suit> : a section drawn in the chosen layout. Its frames are either drawn once per layout,
  // <div class="phone" data-dans="P1 P2">, the others hidden, or redrawn by the page on "mockforme" (bubbles from the
  // section, detail {forme, section}). Chosen = the last 👍 of the layout section, else its « (par défaut) », else the
  // first; ?forme=P3 for a capture. A line above each following section says which, and shows another without voting.
  const formeSec = document.querySelector("section[data-forme]");
  const suits = formeSec ? [...document.querySelectorAll("section[data-suit]")] : [];
  const formeIds = !formeSec ? [] : formeSec.dataset.forme.trim() ? formeSec.dataset.forme.trim().split(/\s+/)
    : [...formeSec.querySelectorAll(".variant")].map(v => v.dataset.id);
  const sectionName = sec => (sec.querySelector(":scope > h2") || {}).textContent || "";
  function chosenForme() {
    if (params.get("forme")) return params.get("forme");
    const liked = formeIds.filter(id => (saved[id] || {}).vote === "like");
    if (saved._forme && liked.includes(saved._forme)) return saved._forme;
    if (liked.length) return liked[liked.length - 1];
    const def = formeIds.find(id => {
      const n = formeSec.querySelector('.variant[data-id="' + id + '"] .vname');
      return n && n.textContent.includes("(par défaut)");
    });
    return def || formeIds[0];
  }
  function drawFormes() {
    const chosen = chosenForme();
    const looks = saved._look || {};
    suits.forEach((sec, i) => {
      const f = (!only && looks[i]) || chosen;
      sec.querySelectorAll("[data-dans]").forEach(el => { el.hidden = !el.dataset.dans.split(/\s+/).includes(f); });
      sec.dispatchEvent(new CustomEvent("mockforme", { bubbles: true, detail: { forme: f, section: sec } }));
      if (only) return;
      let line = sec.querySelector(":scope > .mock-suit");
      if (!line) {
        line = document.createElement("p");
        line.className = "mock-suit";
        const row = sec.querySelector(":scope > .row, :scope > .stack");
        row ? row.before(line) : sec.appendChild(line);
        line.addEventListener("click", e => {
          const v = e.target.closest("[data-voir]");
          if (!v) return;
          saved._look = saved._look || {};
          if (v.dataset.voir === chosenForme()) delete saved._look[i]; else saved._look[i] = v.dataset.voir;
          save();
          drawFormes();
        });
      }
      line.innerHTML = (f === chosen ? "Dessinée dans " + f + ", ta disposition." : "Dessinée dans " + f + ", pour voir.") +
        "<br>Voir dans" + formeIds.map(id => '<span data-voir="' + id + '"' + (id === f ? ' class="on"' : "") + ">" + id + "</span>").join("");
    });
  }
  if (suits.length) drawFormes();

  // ?only=E1 : that variant's frame alone (.phone or .screen; a variant without a frame, whole)
  if (only) {
    const variant = document.querySelector('.variant[data-id="' + only + '"]');
    const frame = variant.querySelector(".phone:not([hidden]), .screen:not([hidden])") || variant;
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
    document.querySelectorAll(".toggle button[data-t]").forEach(b => b.classList.toggle("on", b.dataset.t === t));
  }
  document.querySelectorAll(".toggle button[data-t]").forEach(b => b.onclick = () => setTheme(b.dataset.t));
  setTheme(localStorage.getItem("mock-theme") || pageTheme);

  if (!only) {
    // Smaller frames on a phone. A page made for a phone cannot be pinched under 100 % by
    // the browser itself: two fingers zoom the whole page here (0.35 to 1.5, kept for every page). And, under the title,
    // « Taille réelle | En entier » (kept for every page): whole, each .phone is shrunk to fit in the screen with its
    // variant's name above it. The PC keeps its own Ctrl + wheel. ?only= (captures) is never zoomed.
    const zKey = "mock-zoom", tKey = "mock-taille";
    let zoom = Math.max(0.35, Math.min(1.5, +(localStorage.getItem(zKey) || 1)));
    const whole = () => localStorage.getItem(tKey) === "entier";
    function fitPhones() {
      document.querySelectorAll(".phone").forEach(ph => {
        ph.style.zoom = "";
        if (!whole() || ph.hidden) return;
        ph.style.zoom = Math.min(1, (innerHeight - 120) / (ph.offsetHeight * zoom)).toFixed(3);
      });
    }
    const applyZoom = () => { document.body.style.zoom = zoom === 1 ? "" : zoom; fitPhones(); };
    if (document.querySelector(".phone")) {
      const t = document.createElement("div");
      t.className = "toggle mock-taille";
      t.innerHTML = '<button data-z="reel">Taille réelle</button><button data-z="entier">En entier</button>';
      const h1 = document.querySelector("header.page h1");
      if (h1) h1.after(t);
      const paintT = () => t.querySelectorAll("button").forEach(b => b.classList.toggle("on", (b.dataset.z === "entier") === whole()));
      t.onclick = e => { const b = e.target.closest("button"); if (!b) return; localStorage.setItem(tKey, b.dataset.z); paintT(); fitPhones(); };
      paintT();
      addEventListener("resize", fitPhones);
      addEventListener("mockforme", () => requestAnimationFrame(fitPhones));
    }
    applyZoom();
    const pct = document.createElement("div");
    pct.className = "mock-pct";
    document.body.appendChild(pct);
    let pctT = 0, pinch = null;
    const dist = t => Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);
    addEventListener("touchstart", e => { if (e.touches.length === 2) pinch = { d: dist(e.touches), k: zoom }; }, { passive: true });
    addEventListener("touchmove", e => {
      if (!pinch || e.touches.length !== 2) return;
      e.preventDefault();
      zoom = Math.max(0.35, Math.min(1.5, pinch.k * dist(e.touches) / pinch.d));
      document.body.style.zoom = zoom;
      pct.textContent = Math.round(zoom * 100) + " %";
      pct.classList.add("on");
      clearTimeout(pctT);
      pctT = setTimeout(() => pct.classList.remove("on"), 900);
    }, { passive: false });
    addEventListener("touchend", e => {
      if (!pinch || e.touches.length >= 2) return;
      pinch = null;
      localStorage.setItem(zKey, zoom);
      applyZoom();
    });

    document.querySelectorAll(".variant").forEach(v => {
      const id = v.dataset.id;
      const st = saved[id] || (saved[id] = { vote: "", note: "" });
      const box = document.createElement("div");
      box.innerHTML = '<div class="vote"><button class="like">👍 J’aime</button><button class="nope">👎 Non</button></div>' +
        '<textarea class="note" placeholder="Une remarque sur ' + id + ' ?"></textarea>';
      v.appendChild(box);
      const like = box.querySelector(".like"), nope = box.querySelector(".nope"), note = box.querySelector("textarea");
      const paint = () => { like.classList.toggle("on", st.vote === "like"); nope.classList.toggle("on", st.vote === "nope"); };
      // a vote in the layout section redraws the sections that follow it (the last 👍 wins, the « pour voir » forgotten)
      const forme = () => {
        if (!suits.length || !formeIds.includes(id) || v.closest("section") !== formeSec) return;
        if (st.vote === "like") saved._forme = id;
        delete saved._look;
        save();
        drawFormes();
      };
      like.onclick = () => { st.vote = st.vote === "like" ? "" : "like"; paint(); save(); forme(); };
      nope.onclick = () => { st.vote = st.vote === "nope" ? "" : "nope"; paint(); save(); forme(); };
      note.value = st.note; note.oninput = () => { st.note = note.value; save(); };
      paint();
    });
    const global = document.getElementById("global");
    if (global) { global.value = saved._global || ""; global.oninput = () => { saved._global = global.value; save(); }; }

    const send = document.getElementById("send");
    const label = send.textContent;
    // A vote or remark changed after a send is not sent yet: the button must say so, not stay on "Envoyé ✓"
    // (otherwise a remark typed after sending is never sent, and nothing shows it).
    const unsent = () => { if (send.textContent !== label) send.textContent = label; };
    document.addEventListener("input", unsent);
    document.addEventListener("click", e => { if (e.target.closest(".vote")) unsent(); });
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
      // anything the page set in data-retour inside a variant (a camera framing, a setting): sent as is
      document.querySelectorAll(".variant [data-retour]").forEach(el => {
        if (el.dataset.retour) lines.push(el.closest(".variant").dataset.id + " " + el.dataset.retour);
      });
      if ((saved._global || "").trim()) lines.push("Général : " + saved._global.trim());
      if (!lines.length) { send.textContent = "Rien à envoyer pour l’instant"; setTimeout(() => send.textContent = label, 2000); return; }
      // the layout the following sections were looked at in
      if (suits.length) {
        lines.push("Sections qui suivent la disposition, dessinées dans " + chosenForme());
        Object.entries(saved._look || {}).forEach(([i, f]) => { if (suits[i]) lines.push("« " + sectionName(suits[i]).trim() + " » regardée dans " + f); });
      }
      lines.unshift("Page : " + location.pathname);
      if (document.querySelector(".toggle button[data-t]") && root.dataset.theme !== pageTheme) lines.push("Thème regardé : " + root.dataset.theme);
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
