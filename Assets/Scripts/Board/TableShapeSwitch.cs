using Presentation;
using UnityEngine;
using UnityEngine.Assertions;

namespace Board
{
    /// <summary>
    /// Shows the rectangular table or the round placeholder table, following the local
    /// <see cref="SeatedViewOptions.RoundTable"/> trial option (board task T16). Presentation only: each player sees
    /// the table they picked; the cards and seats do not move.
    /// </summary>
    public class TableShapeSwitch : MonoBehaviour
    {
        [SerializeField] private SeatedViewOptions _options;
        [SerializeField] private GameObject _rectangularTable;
        [SerializeField] private GameObject _roundTable;

        /// <summary>The trial options asset this switch follows.</summary>
        public SeatedViewOptions Options => _options;

        private void Awake()
        {
            Assert.IsNotNull(_options, "TableShapeSwitch._options not wired");
            Assert.IsNotNull(_rectangularTable, "TableShapeSwitch._rectangularTable not wired");
            Assert.IsNotNull(_roundTable, "TableShapeSwitch._roundTable not wired");
        }

        private void OnEnable()
        {
            if (_options != null)
            {
                _options.OnChanged += Apply;
            }
            Apply();
        }

        private void OnDisable()
        {
            if (_options != null)
            {
                _options.OnChanged -= Apply;
            }
        }

        private void Apply()
        {
            bool _round = _options != null && _options.RoundTable;
            if (_rectangularTable != null)
            {
                _rectangularTable.SetActive(!_round);
            }
            if (_roundTable != null)
            {
                _roundTable.SetActive(_round);
            }
        }
    }
}
