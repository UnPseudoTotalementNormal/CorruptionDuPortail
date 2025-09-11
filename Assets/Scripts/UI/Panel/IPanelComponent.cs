namespace UI.Panel
{
    public interface IPanelComponent
    {
        public bool isPanelOpen { get; }
        public void SwitchPanelOpen();
        public void TryOpenPanel();
        public void OpenPanel();
        public void ClosePanel();
    }
}