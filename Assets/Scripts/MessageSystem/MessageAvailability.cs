namespace MessageSystem
{
    /// <summary>
    /// When a player may send an anonymous message: at night, with messages left, once per night. Shared by the send
    /// button and the letters' lamp, so the lamp is lit exactly while the button works.
    /// </summary>
    public static class MessageAvailability
    {
        public static bool CanSend(bool _isNight, int _messagesLeft, bool _hasSentThisTurn) =>
            _isNight && _messagesLeft > 0 && !_hasSentThisTurn;
    }
}
