#region

using System.Collections.Generic;
using System.Linq;
using Board.UI.CharacterBar;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain;
using Extensions;
using UI.Cards;
using UnityEngine;
using UnityEngine.UIElements;

#endregion

namespace UI.RoleCard
{
    /// <summary>
    /// Drives the on-demand role-presentation card (RoleCard.uxml). NOT the role-reveal state screen —
    /// this is a consultable panel opened by clicking a character in the power bar. Binds a <see cref="Role"/>
    /// into the card and handles show/hide.
    ///
    /// Curation (owner-ratified): each personal passive is its own bullet row; any power flagged
    /// <see cref="Power.hideFromRoleCard"/> (a faction win-objective) is omitted; usage counts are static.
    /// Powers granted at runtime are also omitted — showing them would leak that the role is real (not
    /// factice) and which power/role was copied. Two markers cover the copy roles: <see cref="Power.isStolenCopy"/>
    /// (Ugues' Marque d'Hurluberluges, Luma's Mélange des cartes — one-shot copies) and
    /// <see cref="Power.hideFromRoleCardRuntime"/> (L'Incomplet's Réincarnation — permanent grants).
    /// Active powers are shown as numbered pills. Faction display name is TEMP until a Faction
    /// ScriptableObject carries a real displayName + tagline (design-owned narrative).
    ///
    /// The card sits over the existing blurred game backdrop: opening the card fades the shared FrostCanvas
    /// veil in (the uGUI blur consumer of _BackgroundBlurSource); closing fades it out.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RoleCardController : MonoBehaviour
    {
        private const string HiddenClass = "cdp-is-hidden";
        private const string CollapsedClass = "cdp-is-collapsed";
        private const string PipClass = "role-card__pip";
        private const string PipEmptyClass = "role-card__pip--empty";
        private const string PowerClass = "role-card__power";
        private const string PowerNumClass = "role-card__power-num";
        private const string PowerHeadClass = "role-card__power-head";
        private const string PowerTitleClass = "role-card__power-title";
        private const string PowerDescClass = "role-card__power-desc";
        private const string PassiveRowClass = "role-card__passive-row";
        private const string PassiveBulletClass = "role-card__passive-bullet";
        private const string PassiveTextClass = "role-card__passive-text";
        private const string NameLongClass = "role-card__name--long";
        private const string Bullet = "•";
        private const string Star = "★";
        private const int LongNameThreshold = 18;
        private const int DifficultyPips = 3;
        // Longest staggered exit transition (panel: 100ms delay + 300ms) + a small buffer. We collapse
        // (display:none) only after this so the exit animation isn't cut short.
        private const long ExitCollapseDelayMs = 420;

        // Per-faction tint (Sally): the card keeps a dark/gold frame but injects the faction accent colour
        // (FactionData.color) into borders, titles, pills and pips. Shades are computed from that one source.
        private static readonly Color TintDark = new Color(0.149f, 0.122f, 0.078f);      // panel base rgb(38,31,20)
        private static readonly Color TintNearBlack = new Color(0.102f, 0.078f, 0.047f); // pill digit on light accents
        private static readonly Color GoldAccent = new Color(0.749f, 0.604f, 0.322f);    // fallback when no faction colour

        [SerializeField] private UIDocument document;

        [Tooltip("The character bar whose clicks open this card. Wire it in the GameScene.")]
        [SerializeField] private CharactersBar charactersBar;

        [Tooltip("The shared FrostCanvas CanvasGroup (blurred game backdrop). Faded in on open, out on close. Wire it in the GameScene.")]
        [SerializeField] private CanvasGroup frostCanvasGroup;

        [SerializeField] private float frostFadeDuration = 0.25f;

        [Tooltip("Faction presentation data (display name / tagline / icon), keyed by FactionType. Wire the FactionDatabase asset.")]
        [SerializeField] private FactionDatabase factionDatabase;

        [Tooltip("Resolves role.rolePortrait -> Sprite (replaces the old Addressables lookup). Wire the PortraitTable asset.")]
        [SerializeField] private PortraitTable portraitTable;

        [Tooltip("The designer's per-role passive lines (the \"Passif\" block). A role without an entry shows its passive powers' descriptions. Wire the RoleCardTexts asset.")]
        [SerializeField] private RoleCardTexts roleCardTexts;

        private const float PortraitCardWidth = 250f; // the canonical RoleCardElement face used for the portrait

        private VisualElement _root;
        private VisualElement _portrait;
        private RoleCardElement _portraitCard;
        private Label _faction;
        private VisualElement _factionIcon;
        private Label _roleName;
        private VisualElement _difficulty;
        private VisualElement _panel;
        private VisualElement _divider;
        private VisualElement _passiveBlock;
        private Label _passiveLabel;
        private VisualElement _passiveList;
        private VisualElement _powers;
        private bool _initialized;

        // Faction accent + derived shades, computed once per Open() and consumed by the dynamic builders.
        private Color _cAccent = Color.white;
        private Color _cInset = Color.black;
        private Color _cTitle = Color.white;
        private Color _cPillDigit = Color.white;

        private void OnEnable()
        {
            if (charactersBar != null) charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            TryInitialize();
        }

        // Start is a fallback: UIDocument builds its rootVisualElement in its OWN OnEnable, and the order
        // between this component's OnEnable and the UIDocument's is not guaranteed, so the tree can be null
        // on the first pass. By Start (and by the first click) it is always ready.
        private void Start() => TryInitialize();

        private void TryInitialize()
        {
            if (_initialized) return;
            if (document == null) document = GetComponent<UIDocument>();

            var tree = document != null ? document.rootVisualElement : null;
            _root = tree?.Q<VisualElement>("role-card");
            if (_root == null) return;

            _panel = _root.Q<VisualElement>("panel");
            _portrait = _root.Q<VisualElement>("portrait");
            _faction = _root.Q<Label>("faction");
            _factionIcon = _root.Q<VisualElement>("faction-icon");
            _roleName = _root.Q<Label>("role-name");
            _difficulty = _root.Q<VisualElement>("difficulty");
            _divider = _root.Q<VisualElement>("divider");
            _passiveBlock = _root.Q<VisualElement>("passive-block");
            _passiveLabel = _root.Q<Label>("passive-label");
            _passiveList = _root.Q<VisualElement>("passive-list");
            _powers = _root.Q<VisualElement>("powers");

            var closeButton = _root.Q<Button>("close");
            if (closeButton != null) closeButton.clicked += Close;

            // The portrait is the CANONICAL card face (RoleCardElement) so the overlay's card matches the lobby
            // grid / in-game card everywhere. The "portrait" element is just its animated, overlapping host.
            if (_portrait != null)
            {
                _portrait.Clear();
                _portraitCard = new RoleCardElement().SetWidth(PortraitCardWidth);
                _portrait.Add(_portraitCard);
            }

            // Starts hidden + collapsed (see UXML). While collapsed the root must NOT block the world,
            // so picking is Ignore until Open() (then Position so the scrim catches the dismiss click).
            _root.pickingMode = PickingMode.Ignore;
            _root.RegisterCallback<PointerDownEvent>(OnRootPointerDown);

            _initialized = true;
        }

        private void OnDisable()
        {
            if (charactersBar != null) charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
        }

        // A character in the bar was clicked -> show its role card.
        private void OnCharacterBarClicked(Character character)
        {
            if (character != null && character.role != null) Open(character.role);
        }

        /// <summary>Bind a role and reveal the card.</summary>
        public void Open(Role role)
        {
            TryInitialize();
            if (_root == null || role == null) return;

            Bind(role);
            _root.RemoveFromClassList(CollapsedClass);
            _root.pickingMode = PickingMode.Position; // modal: scrim blocks the world + catches dismiss clicks
            // Remove the fade class next frame so the opacity transition actually runs from 0 -> 1.
            _root.schedule.Execute(() => _root.RemoveFromClassList(HiddenClass));

            // Fade the shared blurred backdrop in behind the card.
            if (frostCanvasGroup != null) frostCanvasGroup.DoShowGroup(frostFadeDuration, false, false);
        }

        /// <summary>Animate the card out; it collapses (no layout/input) once the exit finishes.</summary>
        public void Close()
        {
            if (_root == null) return;
            _root.AddToClassList(HiddenClass);
            _root.pickingMode = PickingMode.Ignore;

            if (frostCanvasGroup != null) frostCanvasGroup.DoHideGroup(frostFadeDuration, false, false);

            // Collapse only after the staggered exit finishes (guarded, so a re-open in between cancels it).
            _root.schedule.Execute(CollapseIfHidden).ExecuteLater(ExitCollapseDelayMs);
        }

        // Dismiss only when the scrim itself is clicked, not the panel or its children.
        private void OnRootPointerDown(PointerDownEvent evt)
        {
            if (evt.target == _root) Close();
        }

        private void CollapseIfHidden()
        {
            if (_root != null && _root.ClassListContains(HiddenClass)) _root.AddToClassList(CollapsedClass);
        }

        private void Bind(Role role)
        {
            var roleName = role.roleName.ToString();
            _roleName.text = roleName;
            _roleName.EnableInClassList(NameLongClass, roleName.Length > LongNameThreshold);
            _portraitCard?.SetName(roleName);
            BindFaction(role.factionType);
            BuildDifficulty(role.roleDifficulty);
            BuildPassive(role);
            BuildActivePowers(role);
            BindPortrait(role);
        }

        // Always DifficultyPips dots; the ones past the role's difficulty are dimmed (empty) — same size,
        // opacity only, for clean alignment.
        private void BuildDifficulty(int difficulty)
        {
            _difficulty.Clear();
            for (var i = 1; i <= DifficultyPips; i++)
            {
                var pip = new Label(Star);
                pip.AddToClassList(PipClass);
                if (i > difficulty) pip.AddToClassList(PipEmptyClass);   // empty stars keep the shared dimmed look
                else pip.style.color = _cAccent;                          // filled stars take the faction accent
                _difficulty.Add(pip);
            }
        }

        // Single source of truth for card membership + section: the pure RoleCardPowerVisibility classifier
        // (EditMode-tested in RoleCardPowerVisibilityTests). Categorizes by authoredIsPassive (design-time),
        // NOT the live isPassive — PReincarnation flips the live flag post-use to disable itself, but the power
        // must still read as its authored active power here.
        private static RoleCardSlot SlotOf(Power p) => RoleCardPowerVisibility.Classify(
            authoredIsPassive: p.authoredIsPassive,
            hideFromRoleCard: p.hideFromRoleCard,
            isStolenCopy: p.isStolenCopy.Value,
            hideFromRoleCardRuntime: p.hideFromRoleCardRuntime.Value,
            hasDescription: !string.IsNullOrEmpty(p.powerDescription.ToString()));

        // One bulleted row per passive so distinct passives read as a scannable list instead of a run-on
        // paragraph. The designer's per-role lines win (RoleCardTexts); otherwise one row per passive power, whose
        // membership (incl. the empty-description skip) is owned by SlotOf/RoleCardPowerVisibility.
        private void BuildPassive(Role role)
        {
            _passiveList.Clear();
            IEnumerable<string> lines = roleCardTexts != null && roleCardTexts.TryGetPassives(role.roleID, out var authored)
                ? authored
                : role.powers.Where(p => SlotOf(p) == RoleCardSlot.PassiveRow).Select(p => p.powerDescription.ToString());
            foreach (var desc in lines)
            {
                var row = new VisualElement();
                row.AddToClassList(PassiveRowClass);

                var bullet = new Label(Bullet);
                bullet.AddToClassList(PassiveBulletClass);
                bullet.style.color = _cAccent;

                var text = new Label(desc);
                text.AddToClassList(PassiveTextClass);

                row.Add(bullet);
                row.Add(text);
                _passiveList.Add(row);
            }

            _passiveBlock.EnableInClassList(CollapsedClass, _passiveList.childCount == 0);
        }

        // Each active power: a numbered gold pill (the "I trigger this" marker) + the power name and
        // description. The number lives in the pill, so the title is just the power name.
        private void BuildActivePowers(Role role)
        {
            _powers.Clear();
            var index = 1;
            foreach (var power in role.powers.Where(p => SlotOf(p) == RoleCardSlot.ActivePill))
            {
                var entry = new VisualElement();
                entry.AddToClassList(PowerClass);

                var head = new VisualElement();
                head.AddToClassList(PowerHeadClass);

                var num = new Label(index.ToString());
                num.AddToClassList(PowerNumClass);
                num.style.backgroundColor = _cAccent;
                num.style.color = _cPillDigit;

                var title = new Label(power.powerName.ToString());
                title.AddToClassList(PowerTitleClass);
                title.style.color = _cTitle;

                var desc = new Label(power.powerDescription.ToString());
                desc.AddToClassList(PowerDescClass);

                head.Add(num);
                head.Add(title);
                entry.Add(head);
                entry.Add(desc);
                _powers.Add(entry);
                index++;
            }
        }

        private void BindPortrait(Role role)
        {
            var sprite = portraitTable != null ? portraitTable.Get(role.rolePortrait) : null;
            _portraitCard?.SetPortrait(sprite);
        }

        // Faction line from the FactionDatabase: "displayName : tagline" (tagline optional) + the faction icon,
        // and the per-faction accent tint. Falls back to a name-only label + gold accent when unwired.
        private void BindFaction(FactionType faction)
        {
            FactionData data = null;
            if (factionDatabase != null && factionDatabase.TryGet(faction, out var d)) data = d;

            var text = data != null
                ? (string.IsNullOrEmpty(data.tagline) ? data.displayName : $"{data.displayName} : {data.tagline}")
                : FactionHeader(faction);
            var icon = data != null ? data.icon : null;

            _faction.text = text;
            if (_factionIcon != null)
            {
                _factionIcon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground();
                _factionIcon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            ApplyFactionTint(data != null ? data.color : GoldAccent);
        }

        // Injects the faction accent colour into the card's frame + accents, keeping the body dark/neutral
        // (Sally's ~75/25 rule). Shades are derived from the single source colour f; stores the accent/inset/
        // title/pill-digit for the dynamic builders (pips, passive rows, power pills).
        private void ApplyFactionTint(Color f)
        {
            var fBg = Color.Lerp(f, TintDark, 0.88f);
            var fInset = Color.Lerp(f, TintDark, 0.80f);
            var fTitle = WithLumaAtLeast(Color.Lerp(f, Color.white, 0.35f), 0.55f);
            var fMuted = Color.Lerp(f, Color.white, 0.55f);
            var fDivider = new Color(f.r, f.g, f.b, 0.28f);

            _portraitCard?.SetAccent(f);
            _cAccent = f;
            _cInset = fInset;
            _cTitle = fTitle;
            _cPillDigit = Luma(f) < 0.5f ? Color.white : TintNearBlack;

            if (_panel != null)
            {
                SetBorderColor(_panel, f);
                _panel.style.backgroundColor = fBg;
            }
            if (_portrait != null) SetBorderColor(_portrait, f); // portrait fill stays warm-gold (USS); only the frame tints
            if (_faction != null) _faction.style.color = fMuted;
            if (_roleName != null) _roleName.style.color = fTitle;
            if (_divider != null) _divider.style.backgroundColor = fDivider;
            if (_passiveBlock != null)
            {
                _passiveBlock.style.backgroundColor = fInset;
                _passiveBlock.style.borderLeftColor = f;
            }
            if (_passiveLabel != null) _passiveLabel.style.color = fMuted;

            // Scrollbar thumb — faction-tinted too (owner's call; overrides the gold USS default).
            var dragger = _root?.Q(null, "unity-scroller--vertical")?.Q("unity-dragger");
            if (dragger != null) dragger.style.backgroundColor = f;
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // Lift a colour toward white until it reads legibly (keeps a dark/desaturated faction title readable).
        private static Color WithLumaAtLeast(Color c, float min)
        {
            var col = c;
            for (var i = 0; i < 8 && Luma(col) < min; i++) col = Color.Lerp(col, Color.white, 0.15f);
            return col;
        }

        private static void SetBorderColor(VisualElement e, Color c)
        {
            e.style.borderTopColor = c;
            e.style.borderRightColor = c;
            e.style.borderBottomColor = c;
            e.style.borderLeftColor = c;
        }

        // Name-only fallback when no FactionDatabase entry is available.
        private static string FactionHeader(FactionType faction) => faction switch
        {
            FactionType.anomaly => "Anomalie",
            FactionType.chosen => "Élu",
            FactionType.marginal => "Marginal",
            _ => "Inconnu"
        };
    }
}
