#region

using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain;
using UI.Cards;
using UnityEngine;
using UnityEngine.UIElements;

#endregion

namespace UI.RoleCard
{
    /// <summary>The presentation assets a role sheet reads (all optional: a missing one degrades, never throws).</summary>
    public readonly struct RoleSheetAssets
    {
        public readonly FactionDatabase Factions;
        public readonly PortraitTable Portraits;
        public readonly RoleCardTexts Texts;

        public RoleSheetAssets(FactionDatabase factions, PortraitTable portraits, RoleCardTexts texts)
        {
            Factions = factions;
            Portraits = portraits;
            Texts = texts;
        }

        public FactionData Faction(FactionType faction) =>
            Factions != null && Factions.TryGet(faction, out FactionData data) ? data : null;
    }

    /// <summary>
    /// What a role's "grimoire page" says and how it is laid out (RoleSheet.uss), shared by the in-game role overlay
    /// (<see cref="RoleCardController"/>) and the main menu's role book, so both always show the same text.
    ///
    /// Content rules (owner-ratified, unchanged): the designer's passive lines (RoleCardTexts) win over the passive
    /// powers' descriptions; a power hidden from the card (faction win objective, stolen copy, runtime grant) is left
    /// out; active powers are numbered. The victory lines come straight from the role's WinningConditions, then its
    /// hidden win-objective powers.
    /// </summary>
    public static class RoleSheet
    {
        private const string Bullet = "•";
        private const string Star = "★";
        private const int DifficultyPips = 3;
        private const int LongNameThreshold = 18;

        private static readonly Color Ink = new Color(0.180f, 0.114f, 0.063f); // --cdp-color-ink rgb(46,29,16)
        private static readonly Color GoldAccent = new Color(0.749f, 0.604f, 0.322f); // no faction colour wired

        // ---------------------------------------------------------------- content

        /// <summary>The faction colour turned into an ink that stays legible on parchment (bright greens / oranges darkened).</summary>
        public static Color InkOf(Color faction)
        {
            Color c = Color.Lerp(faction, Ink, 0.38f);
            for (var i = 0; i < 10 && Luma(c) > 0.30f; i++) c = Color.Lerp(c, Ink, 0.18f);
            return c;
        }

        public static Color FactionColor(RoleSheetAssets assets, FactionType faction) =>
            assets.Faction(faction)?.color ?? GoldAccent;

        public static string FactionName(RoleSheetAssets assets, FactionType faction)
        {
            FactionData data = assets.Faction(faction);
            if (data != null)
                return string.IsNullOrEmpty(data.tagline) ? data.displayName : $"{data.displayName} : {data.tagline}";
            return faction switch
            {
                FactionType.anomaly => "Anomalie",
                FactionType.chosen => "Élu",
                FactionType.marginal => "Marginal",
                _ => "Inconnu"
            };
        }

        // Single source of truth for card membership + section: the pure RoleCardPowerVisibility classifier
        // (EditMode-tested). Categorizes by the AUTHORED passive flag, not the live isPassive (PReincarnation flips
        // the live flag post-use to disable itself, but the power must still read as its authored active power).
        public static RoleCardSlot SlotOf(Power p) => RoleCardPowerVisibility.Classify(
            authoredIsPassive: AuthoredIsPassive(p),
            hideFromRoleCard: p.hideFromRoleCard,
            isStolenCopy: p.isStolenCopy.Value,
            hideFromRoleCardRuntime: p.hideFromRoleCardRuntime.Value,
            hasDescription: !string.IsNullOrEmpty(p.powerDescription.ToString()));

        // authoredIsPassive is captured in Power.Awake, which never runs on a power PREFAB: the lobby tablet and the
        // role book show a role from its RoleDataObject (prefab powers), whose serialized isPassive is still the
        // authored value. Without this, every passive power of those cards read as a numbered active power.
        private static bool AuthoredIsPassive(Power p) => p.gameObject.scene.IsValid() ? p.authoredIsPassive : p.isPassive;

        public static List<string> PassiveLines(Role role, RoleCardTexts texts) =>
            texts != null && texts.TryGetPassives(role.roleID, out IReadOnlyList<string> authored)
                ? authored.ToList()
                : role.powers.Where(p => p != null && SlotOf(p) == RoleCardSlot.PassiveRow).Select(p => p.powerDescription.ToString()).ToList();

        public static List<Power> ActivePowers(Role role) =>
            role.powers.Where(p => p != null && SlotOf(p) == RoleCardSlot.ActivePill).ToList();

        /// <summary>
        /// How the role wins, from the game rules: each WinningCondition's own wording, then the hidden win-objective
        /// powers (hideFromRoleCard, e.g. the Mage Occulte's "Abattre le portail"). Duplicates dropped, each line a sentence.
        /// </summary>
        public static List<string> VictoryLines(Role role)
        {
            var lines = new List<string>();
            if (role.winningConditions != null)
                foreach (var condition in role.winningConditions)
                    AddLine(condition?.Description);
            foreach (Power power in role.powers)
                if (power != null && power.hideFromRoleCard) AddLine(power.powerDescription.ToString());
            return lines;

            void AddLine(string line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                line = line.Trim();
                if (!line.EndsWith(".") && !line.EndsWith("!")) line += ".";
                if (!lines.Contains(line)) lines.Add(line);
            }
        }

        public static string Roman(int n) => n switch
        {
            1 => "I.", 2 => "II.", 3 => "III.", 4 => "IV.", 5 => "V.", 6 => "VI.", 7 => "VII.", 8 => "VIII.", 9 => "IX.", 10 => "X.",
            _ => n + "."
        };

        // ---------------------------------------------------------------- fillers (hosts from UXML or code)

        public static void SetName(Label label, string roleName)
        {
            label.text = roleName;
            label.EnableInClassList("role-sheet__name--long", roleName.Length > LongNameThreshold);
        }

        public static void FillDifficulty(VisualElement host, int difficulty, Color ink)
        {
            host.Clear();
            for (var i = 1; i <= DifficultyPips; i++)
            {
                var pip = new Label(Star);
                pip.AddToClassList("role-sheet__pip");
                if (i > difficulty) pip.AddToClassList("role-sheet__pip--empty");
                pip.style.color = ink;
                host.Add(pip);
            }
        }

        /// <summary>The faction's mark on a wax seal of the faction colour.</summary>
        public static void FillSeal(VisualElement seal, VisualElement icon, FactionData data, Color factionColor)
        {
            seal.style.unityBackgroundImageTintColor = Color.Lerp(factionColor, Color.black, 0.25f);
            Sprite sprite = data != null ? data.icon : null;
            icon.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground();
            icon.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static void FillPassives(VisualElement list, IEnumerable<string> lines, Color ink)
        {
            list.Clear();
            foreach (string line in lines)
            {
                var row = new VisualElement();
                row.AddToClassList("role-sheet__passive-row");
                var bullet = new Label(Bullet);
                bullet.AddToClassList("role-sheet__passive-bullet");
                bullet.style.color = ink;
                var text = new Label(line);
                text.AddToClassList("role-sheet__passive-text");
                row.Add(bullet);
                row.Add(text);
                list.Add(row);
            }
        }

        public static void FillPowers(VisualElement list, IEnumerable<Power> powers, Color ink)
        {
            list.Clear();
            var index = 1;
            foreach (Power power in powers)
            {
                var entry = new VisualElement();
                entry.AddToClassList("role-sheet__power");
                var head = new VisualElement();
                head.AddToClassList("role-sheet__power-head");
                var num = new Label(Roman(index++));
                num.AddToClassList("role-sheet__power-num");
                num.style.color = ink;
                var title = new Label(power.powerName.ToString());
                title.AddToClassList("role-sheet__power-title");
                title.style.color = ink;
                var desc = new Label(power.powerDescription.ToString());
                desc.AddToClassList("role-sheet__power-desc");
                head.Add(num);
                head.Add(title);
                entry.Add(head);
                entry.Add(desc);
                list.Add(entry);
            }
        }

        // ---------------------------------------------------------------- whole blocks (book pages)

        /// <summary>
        /// Left page of the book: the role's card, faction seal, name, faction and difficulty. Same pieces as the
        /// overlay's header, stacked for a full page.
        /// </summary>
        public static VisualElement BuildIdentity(Role role, RoleSheetAssets assets, float cardWidth)
        {
            Color faction = FactionColor(assets, role.factionType);
            Color ink = InkOf(faction);
            var root = new VisualElement();
            root.AddToClassList("role-sheet");
            root.AddToClassList("role-sheet__identity");

            Sprite portrait = assets.Portraits != null ? assets.Portraits.Get(role.rolePortrait) : null;
            RoleCardElement card = RoleCardElement.Create(role.roleName.ToString(), portrait, faction).SetWidth(cardWidth);
            card.AddToClassList("role-sheet__card");
            root.Add(card);

            var seal = new VisualElement();
            seal.AddToClassList("role-sheet__seal");
            var icon = new VisualElement();
            icon.AddToClassList("role-sheet__seal-icon");
            seal.Add(icon);
            FillSeal(seal, icon, assets.Faction(role.factionType), faction);
            root.Add(seal);

            var name = new Label();
            name.AddToClassList("role-sheet__name");
            SetName(name, role.roleName.ToString());
            name.style.color = ink;
            root.Add(name);

            var factionLabel = new Label(FactionName(assets, role.factionType));
            factionLabel.AddToClassList("role-sheet__faction");
            root.Add(factionLabel);

            var difficulty = new VisualElement();
            difficulty.AddToClassList("role-sheet__difficulty");
            FillDifficulty(difficulty, role.roleDifficulty, ink);
            root.Add(difficulty);
            return root;
        }

        /// <summary>Right page of the book: victory, passives and powers, scrolling when a role says more than a page.</summary>
        public static VisualElement BuildKit(Role role, RoleSheetAssets assets)
        {
            Color ink = InkOf(FactionColor(assets, role.factionType));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("role-sheet");
            scroll.AddToClassList("role-sheet__kit");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            List<string> victory = VictoryLines(role);
            if (victory.Count > 0)
            {
                var block = new VisualElement();
                block.AddToClassList("role-sheet__victory");
                var label = new Label("Condition de victoire");
                label.AddToClassList("role-sheet__victory-label");
                var text = new Label(string.Join("\n", victory));
                text.AddToClassList("role-sheet__victory-text");
                text.style.color = ink;
                block.Add(label);
                block.Add(text);
                scroll.Add(block);
                scroll.Add(Divider(ink));
            }

            List<string> passives = PassiveLines(role, assets.Texts);
            if (passives.Count > 0)
            {
                scroll.Add(Section("Passif", ink));
                var list = new VisualElement();
                list.AddToClassList("role-sheet__passive");
                FillPassives(list, passives, ink);
                scroll.Add(list);
            }

            List<Power> powers = ActivePowers(role);
            if (powers.Count > 0)
            {
                scroll.Add(Section(powers.Count > 1 ? "Pouvoirs" : "Pouvoir", ink));
                var list = new VisualElement();
                FillPowers(list, powers, ink);
                scroll.Add(list);
            }
            return scroll;
        }

        public static VisualElement Divider(Color ink)
        {
            var divider = new VisualElement();
            divider.AddToClassList("role-sheet__divider");
            divider.style.unityBackgroundImageTintColor = ink;
            return divider;
        }

        private static Label Section(string text, Color ink)
        {
            var label = new Label(text);
            label.AddToClassList("role-sheet__section");
            label.style.color = ink;
            return label;
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }
}
