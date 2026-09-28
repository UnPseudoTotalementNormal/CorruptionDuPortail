using UnityEngine;
using UnityEngine.UIElements;

namespace ModelTest.B.T1
{
    /// <summary>
    /// Optional demo: put on a GameObject with a UIDocument whose source asset is
    /// StatBarExample.uxml. Animates the "health" bar and logs its ChangeEvent.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class StatBarDemo : MonoBehaviour
    {
        [SerializeField] float m_Speed = 20f;

        StatBar m_Health;

        void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            m_Health = root.Q<StatBar>("health");
            if (m_Health != null)
                m_Health.RegisterValueChangedCallback(OnHealthChanged);
        }

        void OnDisable()
        {
            if (m_Health != null)
                m_Health.UnregisterValueChangedCallback(OnHealthChanged);
            m_Health = null;
        }

        void Update()
        {
            if (m_Health == null)
                return;
            // Ping-pong between 0 and max.
            m_Health.value = Mathf.PingPong(Time.time * m_Speed, m_Health.maxValue);
        }

        static void OnHealthChanged(ChangeEvent<float> evt)
        {
            if (evt.newValue <= 0f)
                Debug.Log("[StatBarDemo] Health reached zero.");
        }
    }
}
