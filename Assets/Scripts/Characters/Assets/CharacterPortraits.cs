#region

using System.Collections.Generic;
using CustomAttributes;

#endregion

namespace Characters.Assets
{

public static class CharacterPortraitsValues
{
    public static Dictionary<CharacterPortraits, string> values = new Dictionary<CharacterPortraits, string>
    {
        { CharacterPortraits.ChasseuseDePrime, "Assets/Art/Sprites/Portraits/ChasseuseDePrime.jpg" },
        { CharacterPortraits.DernierGardien, "Assets/Art/Sprites/Portraits/DernierGardien.jpg" },
        { CharacterPortraits.Abyss, "Assets/Art/Sprites/Portraits/Abyss.jpg" },
        { CharacterPortraits.Dryade, "Assets/Art/Sprites/Portraits/Dryade.jpg" },
        { CharacterPortraits.Geolier, "Assets/Art/Sprites/Portraits/Geolier.jpg" },
        { CharacterPortraits.Incomplet, "Assets/Art/Sprites/Portraits/Incomplet.jpg" },
        { CharacterPortraits.Moork, "Assets/Art/Sprites/Portraits/Moork.jpg" },
        { CharacterPortraits.ORIGINE, "Assets/Art/Sprites/Portraits/ORIGINE.jpg" },
        { CharacterPortraits.Orpheline, "Assets/Art/Sprites/Portraits/Orpheline.jpg" },
        { CharacterPortraits.Prophete, "Assets/Art/Sprites/Portraits/Prophete.jpg" },
        { CharacterPortraits.Repenti, "Assets/Art/Sprites/Portraits/Repenti.jpg" },
        { CharacterPortraits.Robot, "Assets/Art/Sprites/Portraits/Robot.jpg" },
        { CharacterPortraits.Technomancien, "Assets/Art/Sprites/Portraits/Technomancien.jpg" },
        { CharacterPortraits.Uges, "Assets/Art/Sprites/Portraits/Uges.jpg" },
        { CharacterPortraits.Vahal, "Assets/Art/Sprites/Portraits/Vahal.jpg" },
        { CharacterPortraits.DrGloubi, "Assets/Art/Sprites/Portraits/DrGloubi.png" },
    };

        [AddressableEnums("Assets/Art/Sprites/Portraits")]
        public enum CharacterPortraits
        {
    Abyss = 1048685107,
    ChasseuseDePrime = 890829750,
    DernierGardien = 1959347720,
    DrGloubi = 295621807,
    Dryade = 1970802544,
    Geolier = 1886328777,
    Incomplet = 1605640824,
    Moork = 403389003,
    ORIGINE = 1674237905,
    Orpheline = 1124117774,
    Prophete = 823899898,
    Repenti = 1256501025,
    Robot = 683198692,
    Technomancien = 632219777,
    Uges = 864506531,
    Vahal = 1384333719,
        }
}

}
