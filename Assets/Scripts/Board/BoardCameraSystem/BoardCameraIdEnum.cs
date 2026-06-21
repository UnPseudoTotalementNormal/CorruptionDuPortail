namespace Board.BoardCameraSystem
{
    public enum BoardCameraIdEnum
    {
        None = 0,
        WatchBoardPov = 1,
        TopBoard = 2,
        LookAtPowers = 3,
        LookAtChat = 4,
        InfoBoard = 5,
        // Seated first-person node: the embodied Vote camera wired INTO the board-camera neighbour graph so
        // the Vote opens on first-person and the player can arrow over to the board overviews and back.
        // APPEND-ONLY — never renumber 0-5 (the inspector + the SerializedDictionary neighbour wiring use the
        // values). The node is driven by AvatarCameraArbiter (it follows whether this id is the live camera).
        SeatedFirstPerson = 6,
    }
}