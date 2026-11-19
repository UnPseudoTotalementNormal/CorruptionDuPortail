// Sticker icons in Wouh's style (dark ink outline + white sticker border, flat fills, one shade).
// Each entry draws inside a 200 x 200 box. window.CDP_STICKERS[name] = inner SVG markup.
(() => {
  const INK = '#1d1111';
  // outline(): white sticker border then dark ink, under the coloured parts
  const wrap = (shape, details = '') => `
    <g stroke-linejoin="round" stroke-linecap="round">
      <g stroke="#fff" stroke-width="24" fill="#fff">${shape}</g>
      <g stroke="${INK}" stroke-width="11" fill="${INK}">${shape}</g>
      <g stroke="none">${shape}</g>
      <g fill="none" stroke="${INK}" stroke-width="5">${details}</g>
    </g>`;

  const S = {};

  // Right guess: a brushed check, green like the lotus
  S.verdict_ok = wrap(
    `<path d="M38 104 L78 146 L164 48 L176 70 L80 176 L24 120 Z" fill="#6fbf5a"/>`,
    `<path d="M48 112 L78 140" stroke="#a9e08f" stroke-width="7"/>`);

  // Wrong guess: Wouh's red cross (same as the refusing paw)
  S.verdict_ko = wrap(
    `<path d="M40 62 L62 40 L100 78 L138 40 L160 62 L122 100 L160 138 L138 160 L100 122 L62 160 L40 138 L78 100 Z" fill="#e83a3a"/>`,
    `<path d="M56 58 L62 52" stroke="#ff8a7a" stroke-width="7"/>`);

  // Healed: a leaf with a drop
  S.soigne = wrap(
    `<path d="M40 160 C40 90 90 40 168 36 C166 112 120 160 40 160 Z" fill="#5fae6a"/>
     <path d="M126 120 C140 140 150 152 150 164 A16 16 0 0 1 118 164 C118 152 120 140 126 120 Z" fill="#8fd0e8"/>`,
    `<path d="M48 152 C80 118 112 86 150 56"/>
     <path d="M82 118 L80 88 M104 96 L130 98 M66 134 L60 112"/>`);

  // Blessing: a golden halo with rays
  S.benediction = wrap(
    `<ellipse cx="100" cy="112" rx="64" ry="26" fill="#f2c14e"/>
     <ellipse cx="100" cy="112" rx="40" ry="12" fill="#3a2414"/>
     <path d="M100 18 L110 58 L90 58 Z" fill="#f2c14e"/>
     <path d="M44 40 L76 72 L62 82 Z" fill="#f2c14e"/>
     <path d="M156 40 L138 82 L124 72 Z" fill="#f2c14e"/>`,
    ``);

  // "Me": a cream paw print (your own cat)
  S.moi = wrap(
    `<path d="M100 100 C130 100 156 128 152 152 C148 172 126 170 100 166 C74 170 52 172 48 152 C44 128 70 100 100 100 Z" fill="#e9dcc4"/>
     <ellipse cx="50" cy="84" rx="18" ry="24" fill="#e9dcc4"/>
     <ellipse cx="82" cy="52" rx="18" ry="25" fill="#e9dcc4"/>
     <ellipse cx="120" cy="52" rx="18" ry="25" fill="#e9dcc4"/>
     <ellipse cx="152" cy="84" rx="18" ry="24" fill="#e9dcc4"/>`,
    `<path d="M70 150 C86 140 114 140 130 150" stroke="#b9a587" stroke-width="7"/>`);

  // Targeted (Orpheline sees who targets her): a wide-open eye
  S.ciblee = wrap(
    `<path d="M14 104 C50 50 150 50 186 104 C150 156 50 156 14 104 Z" fill="#efe3c8"/>
     <circle cx="100" cy="104" r="34" fill="#b8323a"/>
     <circle cx="100" cy="104" r="15" fill="${INK}"/>`,
    `
     <circle cx="88" cy="92" r="4" fill="#fff" stroke="none"/>`);

  // Emote: wave (Coucou)
  S.coucou = wrap(
    `<path d="M70 176 C52 150 50 120 56 96 C60 80 76 78 82 92 L84 58 C86 42 104 42 106 58 L108 84 L112 50 C114 34 134 34 134 52 L132 92 L146 70 C154 58 172 64 166 82 L150 132 C142 158 128 176 104 180 Z" fill="#b9aca8"/>`,
    `<path d="M150 22 L140 40 M176 40 L158 50 M184 70 L166 72" stroke="#f2b632" stroke-width="9"/>
     <path d="M86 140 C100 134 116 134 128 140" stroke="#857673" stroke-width="7"/>`);

  // Emote: cry (Pleurer)
  S.pleurer = wrap(
    `<path d="M100 20 C126 64 152 98 152 128 A52 52 0 0 1 48 128 C48 98 74 64 100 20 Z" fill="#7cc4e6"/>`,
    `<path d="M74 122 C74 108 82 96 92 88" stroke="#d4f0fb" stroke-width="9"/>`);

  // Emote: accuse (Accuser): a claw pointing
  S.accuser = wrap(
    `<path d="M28 132 C26 104 44 90 70 90 L150 82 C164 82 168 100 154 104 L104 110 L120 122 C130 132 122 146 110 142 L112 156 C112 168 92 172 84 164 C60 174 32 164 28 132 Z" fill="#b9aca8"/>
     <path d="M150 82 L186 70 L160 100 Z" fill="#f4ead2"/>`,
    `<path d="M60 128 C72 122 84 122 94 128" stroke="#857673" stroke-width="7"/>`);

  // Awakening timer: a melting candle
  S.bougie = wrap(
    `<path d="M70 82 L130 82 L130 176 L70 176 Z" fill="#efe3c8"/>
     <path d="M70 82 L130 82 L130 104 C122 104 122 128 114 128 C106 128 108 100 100 100 C92 100 92 116 84 116 C76 116 78 100 70 100 Z" fill="#fff7e6"/>
     <path d="M100 18 C116 38 120 52 112 64 C106 72 94 72 88 64 C80 52 86 38 100 18 Z" fill="#f2b632"/>
     <path d="M100 40 C106 50 106 58 100 62 C94 58 94 50 100 40 Z" fill="#fff1b8"/>`,
    `<path d="M100 66 L100 82"/>`);

  // Robot interactions (fallback if Wouh's robot is not used here)
  S.robot = wrap(
    `<rect x="46" y="58" width="108" height="92" rx="18" fill="#8d8f96"/>
     <circle cx="78" cy="102" r="16" fill="#ff7a2a"/>
     <circle cx="122" cy="102" r="16" fill="#3a3a40"/>
     <path d="M100 58 L100 30" fill="none"/>
     <circle cx="100" cy="26" r="10" fill="#ff7a2a"/>`,
    `<path d="M100 58 L100 34"/><path d="M70 132 L130 132"/>`);

  // Emote: fiery pose (Pose ardente)
  S.flamme = wrap(
    `<path d="M100 186 C56 186 34 154 40 118 C44 92 62 78 66 52 C82 66 86 82 84 96 C96 76 98 46 92 16 C130 38 160 76 162 118 C164 158 140 186 100 186 Z" fill="#f07a2a"/>
     <path d="M100 182 C78 182 66 166 70 146 C74 128 90 120 94 100 C110 116 128 132 128 152 C128 170 116 182 100 182 Z" fill="#f2c14e"/>`,
    ``);

  // Healed by Soin Baveux: a green drool drop
  S.bave = wrap(
    `<path d="M30 92 C26 52 70 30 104 34 C146 38 176 60 170 98 C166 124 146 128 140 140 C136 150 140 164 128 168 C116 172 112 156 112 146 C112 136 100 132 92 140 C84 148 92 178 74 180 C58 182 60 160 62 146 C64 132 34 128 30 92 Z" fill="#8cc63f"/>`,
    `<path d="M56 78 C64 60 84 52 104 52" stroke="#d3f29a" stroke-width="9"/>`);

  window.CDP_STICKERS = S;
  window.cdpSticker = (name, size = 64, extra = '') =>
    `<svg class="cdp-stk ${extra}" viewBox="-14 -14 228 228" width="${size}" height="${size}">${S[name]}</svg>`;
})();
