namespace UI.Panel
{
    public interface IPanelComponent : IPanelOpen
    {
        public void SwitchPanelOpen();
        public void TryOpenPanel();
        public void OpenPanel();
        public void ClosePanel();
    }
}