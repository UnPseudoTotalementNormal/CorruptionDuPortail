#region

using System;
using System.Collections.Generic;
using Board.UI.CharacterBar;
using Characters;
using Extensions;
using UI.Cards;
using UnityEngine;
using UnityEngine.UIElements;

#endregion

namespace UI.RoleCard
{
    /// <summary>
    /// Drives the on-demand role presentation overlay (RoleCard.uxml): a grimoire page opened by clicking a character
    /// in the power bar (or a card of the lobby tablet). NOT the role-reveal state screen. Binds a <see cref="Role"/>
    /// into the page and handles show/hide. What the page says and how it looks is <see cref="RoleSheet"/>, shared
    /// with the main menu's role book.
    ///
    /// Curation (owner-ratified): each personal passive is its own bullet row; any power flagged
    /// <see cref="Power.hideFromRoleCard"/> (a faction win-objective) is omitted; usage counts are static.
    /// Powers granted at runtime are also omitted — showing them would leak that the role is real (not
    /// factice) and which power/role was copied. Two markers cover the copy roles: <see cref="Power.isStolenCopy"/>
    /// (Ugues' Marque d'Hurluberluges, Luma's Mélange des cartes — one-shot copies) and
    /// <see cref="Power.hideFromRoleCardRuntime"/> (L'Incomplet's Réincarnation — permanent grants).
    ///
    /// The page sits over the existing blurred game backdrop: opening fades the shared FrostCanvas veil in (the uGUI
    /// blur consumer of _BackgroundBlurSource); closing fades it out.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RoleCardController : MonoBehaviour
    {
        private const string HiddenClass = "cdp-is-hidden";
        private const string CollapsedClass = "cdp-is-collapsed";
        // Longest staggered exit transition (portrait: 120ms delay + 340ms) + a small buffer. We collapse
        // (display:none) only after this so the exit animation isn't cut short.
        private const long ExitCollapseDelayMs = 500;
        private const float PortraitCardWidth = 260f; // the canonical RoleCardElement face pinned on the page

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

        [Tooltip("Knowledge store: tells whether the viewer knows a clicked role is fake. Wire the scene's GameInfoRevealer.")]
        [SerializeField] private GameLogic.GameInfoRevealer gameInfoRevealer;

        [Tooltip("Show the short victory condition above the kit. Off on the in-game card.")]
        [SerializeField] private bool showVictoryCondition;

        /// <summary>Raised when the card is dismissed (close button or click outside the page).</summary>
        public event Action Closed;

        private VisualElement _root;
        private VisualElement _portrait;
        private RoleCardElement _portraitCard;
        private VisualElement _seal;
        private VisualElement _factionIcon;
        private Label _faction;
        private Label _roleName;
        private Label _fakeBadge;
        private VisualElement _difficulty;
        private VisualElement _divider;
        private VisualElement _victoryBlock;
        private Label _victoryText;
        private VisualElement _passiveBlock;
        private Label _passiveLabel;
        private VisualElement _passiveList;
        private VisualElement _powersBlock;
        private Label _powersLabel;
        private VisualElement _powers;
        private bool _initialized;

        private RoleSheetAssets Assets => new RoleSheetAssets(factionDatabase, portraitTable, roleCardTexts);

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

            _portrait = _root.Q<VisualElement>("portrait");
            _seal = _root.Q<VisualElement>("seal");
            _factionIcon = _root.Q<VisualElement>("faction-icon");
            _faction = _root.Q<Label>("faction");
            _roleName = _root.Q<Label>("role-name");
            _fakeBadge = _root.Q<Label>("fake-badge");
            _difficulty = _root.Q<VisualElement>("difficulty");
            _divider = _root.Q<VisualElement>("divider");
            _victoryBlock = _root.Q<VisualElement>("victory-block");
            _victoryText = _root.Q<Label>("victory-text");
            _passiveBlock = _root.Q<VisualElement>("passive-block");
            _passiveLabel = _root.Q<Label>("passive-label");
            _passiveList = _root.Q<VisualElement>("passive-list");
            _powersBlock = _root.Q<VisualElement>("powers-block");
            _powersLabel = _root.Q<Label>("powers-label");
            _powers = _root.Q<VisualElement>("powers");

            var closeButton = _root.Q<Button>("close");
            if (closeButton != null) closeButton.clicked += Close;

            // The pinned card is the CANONICAL card face (RoleCardElement) so the overlay's card matches the lobby
            // grid / in-game card everywhere. The "portrait" element is just its animated host.
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
            if (character != null && character.role != null) Open(character.role, IsKnownFake(character));
        }

        // The viewer (or the bot the host possesses) was told this character's role is fake.
        private bool IsKnownFake(Character character) =>
            character.isFake && gameInfoRevealer != null &&
            gameInfoRevealer.GetCharacterInfo(character.ownerClientId.Value).isFakeRevealed > GameLogic.RevealLevel.False;

        /// <summary>Bind a role and reveal the card.</summary>
        public void Open(Role role) => Open(role, false);

        /// <summary>Bind a role and reveal the card; <paramref name="knownFake"/> adds the "rôle factice" line.</summary>
        public void Open(Role role, bool knownFake)
        {
            TryInitialize();
            if (_root == null || role == null) return;

            Bind(role);
            _fakeBadge?.EnableInClassList(CollapsedClass, !knownFake);
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
            Closed?.Invoke();
        }

        // Dismiss only when the scrim itself is clicked, not the page or its children.
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
            RoleSheetAssets assets = Assets;
            Color faction = RoleSheet.FactionColor(assets, role.factionType);
            Color ink = RoleSheet.InkOf(faction);
            string roleName = role.roleName.ToString();

            RoleSheet.SetName(_roleName, roleName);
            _roleName.style.color = ink;
            _faction.text = RoleSheet.FactionName(assets, role.factionType);
            if (_seal != null && _factionIcon != null) RoleSheet.FillSeal(_seal, _factionIcon, assets.Faction(role.factionType), faction);
            RoleSheet.FillDifficulty(_difficulty, role.roleDifficulty, ink);
            if (_divider != null) _divider.style.unityBackgroundImageTintColor = ink;

            BindVictory(role, ink);

            List<string> passives = RoleSheet.PassiveLines(role, roleCardTexts);
            RoleSheet.FillPassives(_passiveList, passives, ink);
            _passiveLabel.style.color = ink;
            _passiveBlock.EnableInClassList(CollapsedClass, passives.Count == 0);

            var powers = RoleSheet.ActivePowers(role);
            RoleSheet.FillPowers(_powers, powers, ink);
            if (_powersLabel != null)
            {
                _powersLabel.text = powers.Count > 1 ? "Pouvoirs" : "Pouvoir";
                _powersLabel.style.color = ink;
            }
            _powersBlock?.EnableInClassList(CollapsedClass, powers.Count == 0);

            if (_portraitCard != null)
            {
                _portraitCard.SetName(roleName);
                _portraitCard.SetAccent(faction);
                _portraitCard.SetPortrait(portraitTable != null ? portraitTable.Get(role.rolePortrait) : null);
            }
        }

        // The victory lines (RoleSheet.VictoryLines, from the role's WinningConditions). Hidden on the in-game card
        // (showVictoryCondition off) and when nothing describes the role's victory.
        private void BindVictory(Role role, Color ink)
        {
            if (_victoryBlock == null) return;
            List<string> lines = showVictoryCondition ? RoleSheet.VictoryLines(role) : new List<string>();
            _victoryBlock.EnableInClassList(CollapsedClass, lines.Count == 0);
            if (lines.Count == 0) return;
            _victoryText.text = string.Join("\n", lines);
            _victoryText.style.color = ink;
        }
    }
}
