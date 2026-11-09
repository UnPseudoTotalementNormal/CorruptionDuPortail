using System.Text;
using Characters.Powers;

namespace TooltipSystem
{
    /// <summary>
    /// The bubble text of a power, written in one place (it was copied in both power bars and the link parser): its
    /// description with its {var:…} values filled in, then one link per power component ("En chaîne", "Concentration"),
    /// whose card the bubble opens beside it.
    /// </summary>
    public static class PowerTooltipText
    {
        public static string Describe(Power _power)
        {
            if (_power == null)
            {
                return null;
            }
            var _text = new StringBuilder(TooltipLinkParser.ParseVars(_power, _power.powerDescription.ToString()));
            var _components = _power.powerComponents;
            if (_components == null || _components.Count == 0)
            {
                return _text.ToString();
            }

            _text.Append('\n');
            for (int _index = 0; _index < _components.Count; _index++)
            {
                if (_index > 0)
                {
                    _text.Append(", ");
                }
                _text.Append("<link=powercomponent_").Append(_power.ownerClientId.Value).Append('_')
                    .Append(_power.NetworkObjectId).Append('_').Append(_index).Append('>')
                    .Append(_components[_index].componentName.ToString()).Append("</link>");
            }
            return _text.ToString();
        }
    }
}
