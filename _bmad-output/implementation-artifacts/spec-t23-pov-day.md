# T23: the day in first person only ("Créer le nouveau système de jour en POV only")

Board task T23 (Wouh, Haute Priorité). Reference: the team's Canva board "Prototype de l'interface in game", the frame
"Touche 1 / Touche 2 / Emote = E / Tableur : T" (its visuals are placeholders, never reproduced). Decided with Poyo on
2026-10-21 by questions, then on mockups `tools/maquettes/jour-pov/` (v1: T3, K2, R3, V2, H2; v2: A2, M2 + M3).

## Decisions

- **Day = the seated Embodied mode** (the vote and its recap) **and the chaining that follows** (the table stays away
  until the night; Poyo, 2026-10-21). The night does not change.
- **The chaining, seen seated by everyone (T19 points 2 to 4, redesigned by Poyo on 2026-10-21, shipped in the same PR).**
  Mockups `tools/maquettes/enchainement/v4.html`: D2 + Y1. Both chainings are day: the morning one too (the table goes
  before it), and the victory check between it and the vote keeps the seated view. Everyone stays in their seat, the
  chained player too (their own body, hidden in first person, is seen leaving). A force lifts the cat slowly while
  carrying it to the middle of the circle (easing in); on each screen it turns to face that player. Its real card leaves
  the row, comes in front of it and turns over on its role (its own player knows it: no turn); the card goes back, the
  cat falls to the floor, its flattest pose held half a second, it gets up, and under the day's black it is back on its
  seat. The room's lights go down, one light follows the cat. Heads stay free. No sucked-out sound.
  Where it floats: on the line from each player's eye to the middle of the circle, the middle itself unless the cat
  would not fit the screen (your seat is 14 m from the middle, a cat is about 15 m high): then pushed back until it
  takes 68 % of the screen's height (D2 today; D1 if the circle grows, Poyo).
- **The table goes away (T3).** The cards fly from the table to the screen first, then the dark swallows the table:
  under the black the table, its board, the cork wall (and the round-table trial) disappear, with the world UI lying
  on it (powers bar, skip button). The vote candle goes down to the floor, ahead of the player. When the cats' lights
  come back, only the players are left, in a circle.
- **The view rises (K2):** once the table is gone, the seated rest pitch goes from 35° to 20° over 1.2 s (the mouse
  keeps control). Back to 35° at night.
- **Two rows on screen, both open when the day starts, stacked (R3, big cards):**
  - key **1**: the players' cards (the real board cards, everything they show: known roles, marks, "A voté");
  - key **2**: the roles of the game (the cork's characters bar, grouped by side) with the two counters (corruption,
    robot).
  A closed row slides out under the screen; with the roles closed the cards come down. At night the cards fly back
  onto the table and the bar back onto the cork.
- **Vote on the cats:** the seated reticle on a cat + click. The cards are only to look at (their vote button is hidden;
  "A voté" / "N'a pas voté" stays). A chained or eliminated player cannot be voted for (same rule as the old buttons).
- **Hover (H2):** pointing at a cat (or at its card with a free cursor) swells the cat's light; its card stands out
  (the others dim).
- **Anonymous vote**, on by default, an option on the `VoteState` asset (`anonymousVote`) until a lobby menu exists:
  - on: everyone sees who has voted ("A voté" on the cards, A2), not for whom;
  - off: a cat that voted holds the game's accusing pose (the "Accuser" emote: Accusing then Accused Idle, V2), for the
    vote and its recap.
  - in both cases my own vote, for me alone: my voted player's card stays raised (M2) and the cat keeps the highlight
    outline (M3).
- **Skip the vote = a key (P)** by day: the board's "Passer le vote" button lay on the table (Poyo, 2026-10-21).
- **Keys by day:** 1, 2, **P**, **T** (opens the tablet straight on the players' table; Tab is unchanged), **E** (the emote
  wheel, was T). No camera switching with the arrows at all (Poyo, 2026-10-21): first person by day, the top view at night. The key hints in
  the corner show 1, 2, T and P by day (new drawings: `tools/ui/gen_day_key_icons.py`).

## Code

| What | Where |
|---|---|
| Screen rows layout (pure, tested) | `Domain/ScreenRowLayout.cs`, `Tests/Editor/ScreenRowLayoutTests.cs` |
| Rows on screen (cards + bar + counters, keys 1 / 2, raised / lit card) | `Presentation/DayScreenRows.cs` (BoardManager layout override `Board/ICardLayoutOverride.cs`) |
| The day's transition (fly, black, table, candle, pitch) | `Presentation/DayTableTransition.cs` |
| Vote and hover on the cats, my vote | `Presentation/DayCatVoting.cs`, `Avatars/CatPointerTarget.cs` (trigger capsule on `PlayerAvatar.prefab`) |
| Accusing pose (server, NetworkAnimator) | `PlayerAvatar.SetAccusingServer`, `AvatarManager.UpdateVotePoses` |
| Options | `VoteState.anonymousVote`, `VoteState.voteOnCats` |
| Keys / hints | `TabletToggleInput` (T), `EmoteWheelInput` (E), `KeyHints` + `KeyHintsHud` |
| Smaller hooks | `AvatarEmbodiedCamera.SetRestPitch`, `VoteCandle.SetOnFloor`, `CatSpotlights.Boosted`, `VoteCanvas.ActivateVoteCanvas(bool)`, `CardVisualComponents.inScreenRow`, `AvatarCameraArbiter` (no board nav by day) |
| Chaining moment (T19) | `Domain/ChainRevealTimeline.cs` (+ tests), `Presentation/ChainRevealDirector.cs` (via `ChainingManager.RevealDirector`, played by `ChainingState`; samples the artist's chained clips itself), `AvatarHopMotion.SetWorldPose`, `AvatarVisibilityController.SetFeatured`, `CatSpotlights.Darken/Lifted`, `DayScreenRows` spotlight (`BeginSpotlight`, `TryGetSlot`, `SetSpotlightPose`, `EndSpotlight`), `DayTableTransition.DaySettled/BlackOutAsync`, `AvatarManager.RingCenter`; `AvatarCameraModePolicy` (chainings seated, a victory check keeps the view); temporary sounds ChainLift / ChainSplat in the FMOD bank `ChainReveal` (`tools/fmod/t19-chain-reveal.js`) |
| Autoplay real-input vote | `AutoplayDriverRealInput.VoteByInput` aims at the cat |

Scene: `---GameVisuals---/DayPresentation` holds the three presenters, `---GameVisuals---/ChainRevealDirector` the
chaining moment (and the light that follows the cat); lane A, wired.

## Open

- Playtest: the flight, the black, the rows' size on other screens, the "A voté" panel under a card in the row.
- The lobby option for the anonymous vote (menu).
- How the closed tablet (lying behind the cork) looks once the table is gone.
- T19: real sounds for the lift and the splatch (the bank `ChainReveal` holds synthesized placeholders).
