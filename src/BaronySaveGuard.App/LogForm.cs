namespace BaronySaveGuard.App;

internal sealed class LogForm : Form
{
    private readonly TextBox _text;

    public LogForm(IEnumerable<GuardEvent> history)
    {
        Text = "Barony Save Guard - Activity";
        Size = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Shield;

        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 9),
        };
        Controls.Add(_text);
        foreach (var e in history) Append(e);
    }

    public void Append(GuardEvent e)
    {
        _text.AppendText(e + Environment.NewLine);
    }
}
