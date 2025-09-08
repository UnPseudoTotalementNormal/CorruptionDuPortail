namespace UI.Panel
{
    public interface IPanelComponent
    {
        public bool TryOpenPanel();
        public void OpenPanel();
        public void ClosePanel();
    }
}