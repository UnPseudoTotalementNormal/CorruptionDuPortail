#region

using System.Linq;
using Board.UI.CharacterBar;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using Extensions;
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
        private const string PowerBodyClass = "role-card__power-body";
        private const string PowerTitleClass = "role-card__power-title";
        private const string PowerDescClass = "role-card__power-desc";
        private const string PassiveRowClass = "role-card__passive-row";
        private const string PassiveBulletClass = "role-card__passive-bullet";
        private const string PassiveTextClass = "role-card__passive-text";
        private const string NameLongClass = "role-card__name--long";
        private const string Bullet = "•";
        private const int LongNameThreshold = 18;
        private const int DifficultyPips = 3;
        // Longest staggered exit transition (panel: 100ms delay + 300ms) + a small buffer. We collapse
        // (display:none) only after this so the exit animation isn't cut short.
        private const long ExitCollapseDelayMs = 420;

        [SerializeField] private UIDocument document;

        [Tooltip("The character bar whose clicks open this card. Wire it in the GameScene.")]
        [SerializeField] private CharactersBar charactersBar;

        [Tooltip("The shared FrostCanvas CanvasGroup (blurred game backdrop). Faded in on open, out on close. Wire it in the GameScene.")]
        [SerializeField] private CanvasGroup frostCanvasGroup;

        [SerializeField] private float frostFadeDuration = 0.25f;

        [Tooltip("Faction presentation data (display name / tagline / icon), keyed by FactionType. Wire the FactionDatabase asset.")]
        [SerializeField] private FactionDatabase factionDatabase;

        private VisualElement _root;
        private VisualElement _portrait;
        private Label _faction;
        private VisualElement _factionIcon;
        private Label _roleName;
        private VisualElement _difficulty;
        private VisualElement _passiveBlock;
        private VisualElement _passiveList;
        private VisualElement _powers;
        private bool _initialized;

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
            _faction = _root.Q<Label>("faction");
            _factionIcon = _root.Q<VisualElement>("faction-icon");
            _roleName = _root.Q<Label>("role-name");
            _difficulty = _root.Q<VisualElement>("difficulty");
            _passiveBlock = _root.Q<VisualElement>("passive-block");
            _passiveList = _root.Q<VisualElement>("passive-list");
            _powers = _root.Q<VisualElement>("powers");

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
            BindFaction(role.factionType);
            BuildDifficulty(role.roleDifficulty);
            BuildPassive(role);
            BuildActivePowers(role);
            BindPortraitAsync(role).Forget();
        }

        // Always DifficultyPips dots; the ones past the role's difficulty are dimmed (empty) — same size,
        // opacity only, for clean alignment.
        private void BuildDifficulty(int difficulty)
        {
            _difficulty.Clear();
            for (var i = 1; i <= DifficultyPips; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList(PipClass);
                if (i > difficulty) pip.AddToClassList(PipEmptyClass);
                _difficulty.Add(pip);
            }
        }

        // One bulleted row per passive (isPassive && !hideFromRoleCard) so distinct passives read as a
        // scannable list instead of a run-on paragraph. The win-objective power is excluded via hideFromRoleCard.
        private void BuildPassive(Role role)
        {
            _passiveList.Clear();
            foreach (var power in role.powers.Where(p => p.isPassive && !p.hideFromRoleCard))
            {
                var desc = power.powerDescription.ToString();
                if (string.IsNullOrEmpty(desc)) continue;

                var row = new VisualElement();
                row.AddToClassList(PassiveRowClass);

                var bullet = new Label(Bullet);
                bullet.AddToClassList(PassiveBulletClass);

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
            foreach (var power in role.powers.Where(p => !p.isPassive && !p.hideFromRoleCard))
            {
                var entry = new VisualElement();
                entry.AddToClassList(PowerClass);

                var num = new Label(index.ToString());
                num.AddToClassList(PowerNumClass);

                var body = new VisualElement();
                body.AddToClassList(PowerBodyClass);

                var title = new Label(power.powerName.ToString());
                title.AddToClassList(PowerTitleClass);

                var desc = new Label(power.powerDescription.ToString());
                desc.AddToClassList(PowerDescClass);

                body.Add(title);
                body.Add(desc);
                entry.Add(num);
                entry.Add(body);
                _powers.Add(entry);
                index++;
            }
        }

        private async UniTaskVoid BindPortraitAsync(Role role)
        {
            var sprite = await role.GetRolePortrait();
            if (sprite != null && _portrait != null)
                _portrait.style.backgroundImage = new StyleBackground(sprite);
        }

        // Faction line from the FactionDatabase: "displayName : tagline" (tagline optional) + the faction icon.
        // Falls back to a name-only label if the database is unwired or missing the entry.
        private void BindFaction(FactionType faction)
        {
            string text;
            Sprite icon = null;
            if (factionDatabase != null && factionDatabase.TryGet(faction, out var data) && data != null)
            {
                text = string.IsNullOrEmpty(data.tagline) ? data.displayName : $"{data.displayName} : {data.tagline}";
                icon = data.icon;
            }
            else
            {
                text = FactionHeader(faction);
            }

            _faction.text = text;
            if (_factionIcon != null)
            {
                _factionIcon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground();
                _factionIcon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
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
