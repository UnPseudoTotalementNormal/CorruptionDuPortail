#region

using System.Linq;
using Board.UI.CharacterBar;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
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
    /// Curation (owner-ratified, mockup-faithful): the personal passives render FUSED into one block; any
    /// power flagged <see cref="Power.hideFromRoleCard"/> (a faction win-objective) is omitted; usage counts
    /// are static (no live counter). Faction display name is TEMP until a Faction ScriptableObject carries a
    /// real displayName + tagline (design-owned narrative).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RoleCardController : MonoBehaviour
    {
        private const string HiddenClass = "cdp-is-hidden";
        private const string CollapsedClass = "cdp-is-collapsed";
        private const string PipClass = "role-card__pip";
        private const string PowerClass = "role-card__power";
        private const string PowerTitleClass = "role-card__power-title";
        private const string PowerDescClass = "role-card__power-desc";
        private const string PassiveRowClass = "role-card__passive-row";
        private const string PassiveBulletClass = "role-card__passive-bullet";
        private const string PassiveTextClass = "role-card__passive-text";
        private const string Bullet = "•";

        [SerializeField] private UIDocument document;

        [Tooltip("The character bar whose clicks open this card. Wire it in the GameScene.")]
        [SerializeField] private CharactersBar charactersBar;

        private VisualElement _root;
        private VisualElement _portrait;
        private Label _faction;
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
            _roleName = _root.Q<Label>("role-name");
            _difficulty = _root.Q<VisualElement>("difficulty");
            _passiveBlock = _root.Q<VisualElement>("passive-block");
            _passiveList = _root.Q<VisualElement>("passive-list");
            _powers = _root.Q<VisualElement>("powers");

            // Starts hidden + collapsed (see UXML). While collapsed the root must NOT block the world,
            // so picking is Ignore until Open() (then Position so the scrim catches the dismiss click).
            _root.pickingMode = PickingMode.Ignore;
            _root.RegisterCallback<PointerDownEvent>(OnRootPointerDown);
            _root.RegisterCallback<TransitionEndEvent>(OnRootTransitionEnd);

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
        }

        /// <summary>Fade the card out; it collapses (no layout/input) once the transition ends.</summary>
        public void Close()
        {
            if (_root == null) return;
            _root.AddToClassList(HiddenClass);
            _root.pickingMode = PickingMode.Ignore;
        }

        // Dismiss only when the scrim itself is clicked, not the panel or its children.
        private void OnRootPointerDown(PointerDownEvent evt)
        {
            if (evt.target == _root) Close();
        }

        private void OnRootTransitionEnd(TransitionEndEvent evt)
        {
            if (_root.ClassListContains(HiddenClass)) _root.AddToClassList(CollapsedClass);
        }

        private void Bind(Role role)
        {
            _roleName.text = role.roleName.ToString();
            _faction.text = FactionHeader(role.factionType); // TEMP — replace with Faction SO displayName + tagline
            BuildDifficulty(role.roleDifficulty);
            BuildPassive(role);
            BuildActivePowers(role);
            BindPortraitAsync(role).Forget();
        }

        private void BuildDifficulty(int difficulty)
        {
            _difficulty.Clear();
            for (var i = 0; i < difficulty; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList(PipClass);
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

        private void BuildActivePowers(Role role)
        {
            _powers.Clear();
            var index = 1;
            foreach (var power in role.powers.Where(p => !p.isPassive && !p.hideFromRoleCard))
            {
                var entry = new VisualElement();
                entry.AddToClassList(PowerClass);

                var title = new Label($"Pouvoir {index} : {power.powerName}");
                title.AddToClassList(PowerTitleClass);

                var desc = new Label(power.powerDescription.ToString());
                desc.AddToClassList(PowerDescClass);

                entry.Add(title);
                entry.Add(desc);
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

        // TEMP faction header. These are the existing in-game faction names (top bar); the tagline
        // ("The Evil Guys" in the mockup) is design-owned narrative and stays empty until a Faction SO exists.
        private static string FactionHeader(FactionType faction) => faction switch
        {
            FactionType.anomaly => "Anomalie",
            FactionType.chosen => "Élu",
            FactionType.marginal => "Marginal",
            _ => "Inconnu"
        };
    }
}
