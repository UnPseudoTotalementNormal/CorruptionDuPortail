# T23: the day in first person only ("Créer le nouveau système de jour en POV only")

Board task T23 (Wouh, Haute Priorité). Reference: the team's Canva board "Prototype de l'interface in game", the frame
"Touche 1 / Touche 2 / Emote = E / Tableur : T" (its visuals are placeholders, never reproduced). Decided with Poyo on
2026-10-21 by questions, then on mockups `tools/maquettes/jour-pov/` (v1: T3, K2, R3, V2, H2; v2: A2, M2 + M3).

## Decisions

- **Day = the seated Embodied mode** (the vote and its recap) **and the chaining that follows** (the table stays away
  until the night; Poyo, 2026-10-21). The chaining sequence itself is untouched; its card animation is abandoned (T19).
  The night does not change.
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
| Autoplay real-input vote | `AutoplayDriverRealInput.VoteByInput` aims at the cat |

Scene: `---GameVisuals---/DayPresentation` holds the three presenters (lane A, wired).

## Open

- Playtest: the flight, the black, the rows' size on other screens, the "A voté" panel under a card in the row.
- The lobby option for the anonymous vote (menu).
- How the closed tablet (lying behind the cork) looks once the table is gone.
