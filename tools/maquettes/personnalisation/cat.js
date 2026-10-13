// The cat of the mockups: four stacked layers rendered from Corrupted_Cats.blend (same camera, the other parts held
// out), so any mix of parts composes like the real model. Names are placeholders (the art's file names in French).
const PARTS = [
  { key: "body", label: "Pelage", options: [
    { id: "bw", name: "Noir et blanc" }, { id: "fiery", name: "Ardent" },
    { id: "galactic", name: "Galactique" }, { id: "mage", name: "Mage" } ] },
  { key: "outfit", label: "Tenue", options: [
    { id: "bw", name: "Cape et nœud rouge" }, { id: "fiery", name: "Cape ardente" },
    { id: "galactic", name: "Nœud galactique" }, { id: "mage", name: "Cape de mage" }, { id: "", name: "Sans tenue" } ] },
  { key: "eyes", label: "Yeux", options: [
    { id: "bw", name: "Yeux de la portée" }, { id: "fiery", name: "Yeux ardents" },
    { id: "galactic", name: "Yeux galactiques" }, { id: "mage", name: "Yeux de mage" } ] },
  { key: "mouth", label: "Bouche", options: [
    { id: "", name: "Sans bouche" }, { id: "galactic", name: "Sourire galactique" }, { id: "mage", name: "Bouche de mage" } ] },
];
const START = { body: "bw", outfit: "fiery", eyes: "mage", mouth: "mage" };
const img = (part, id) => "img/" + part + "_" + id + ".webp";

// A cat element (layers) bound to a state; every view of a screen shares that screen's state.
class CatState {
  constructor() { this.v = { ...START }; this.listeners = []; }
  get(part) { return this.v[part]; }
  set(part, id) { this.v[part] = id; this.listeners.forEach(f => f()); }
  step(part, dir) {
    const opts = PARTS.find(p => p.key === part).options;
    const i = opts.findIndex(o => o.id === this.v[part]);
    this.set(part, opts[(i + dir + opts.length) % opts.length].id);
  }
  name(part) { return PARTS.find(p => p.key === part).options.find(o => o.id === this.v[part]).name; }
  on(f) { this.listeners.push(f); f(); }
}

function catElement(state, cls) {
  const el = document.createElement("div");
  el.className = "cat " + (cls || "");
  const layers = {};
  for (const p of ["body", "outfit", "eyes", "mouth"]) {
    const i = document.createElement("img");
    i.className = "layer layer--" + p;
    layers[p] = i;
    el.appendChild(i);
  }
  state.on(() => {
    for (const p in layers) {
      const id = state.get(p);
      layers[p].style.visibility = id ? "visible" : "hidden";
      if (id) layers[p].src = img(p, id);
    }
  });
  return el;
}

// A vignette of one option: the part alone, framed on its box in the 440 x 575 layer (alpha bounds of all options).
const BOX = { body: [5, 7, 434, 550], outfit: [50, 300, 330, 570], eyes: [39, 150, 321, 351], mouth: [80, 300, 226, 380] };
function vignette(part, id, w, h) {
  const v = document.createElement("div");
  v.className = "vign";
  if (!id) { v.classList.add("vign--none"); return v; }
  const [x0, y0, x1, y1] = BOX[part];
  const k = Math.min(w / (x1 - x0), h / (y1 - y0)) * 0.92;
  v.style.backgroundImage = "url(" + img(part, id) + ")";
  v.style.backgroundSize = (440 * k) + "px " + (575 * k) + "px";
  v.style.backgroundPosition = (w / 2 - (x0 + x1) / 2 * k) + "px " + (h / 2 - (y0 + y1) / 2 * k) + "px";
  return v;
}
