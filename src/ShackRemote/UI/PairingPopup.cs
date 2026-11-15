using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>
/// Kopplungscode als eigenes kleines Fenster im Vordergrund. Wird im Dienstbetrieb verwendet, wenn das
/// Agent-Fenster ausgeblendet ist (nur Symbol im Infobereich). Schließt sich nach Ende des Kopplungsversuchs.
/// </summary>
public sealed class PairingPopup : Form
{
    /// <param name="clientName">Rechnername des Geräts, das sich koppeln möchte.</param>
    /// <param name="code">6-stelliger Kopplungscode.</param>
    public PairingPopup(string clientName, string code)
    {
        LayoutKit.PrepareForm(this, "ShackRemote – Kopplung");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var text = new Label
        {
            Text = $"{clientName} möchte sich mit diesem PC verbinden.\nDiesen Code dort eingeben (2 Minuten gültig):",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Margin = new Padding(3, 3, 3, 10),
        };
        var codeLabel = new Label
        {
            Text = $"{code[..3]} {code[3..]}",
            AutoSize = true,
            Font = new Font("Segoe UI", 32F, FontStyle.Bold),
            ForeColor = Theme.Accent,
            Anchor = AnchorStyles.Top,
            Margin = new Padding(3, 0, 3, 10),
        };
        var hint = new Label
        {
            Text = "Nur eingeben, wenn du selbst gerade die Verbindung aufbaust.",
            AutoSize = true,
            ForeColor = Theme.SubtleText,
            Margin = new Padding(3, 0, 3, 3),
        };

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(20),
        };
        root.Controls.Add(text);
        root.Controls.Add(codeLabel);
        root.Controls.Add(hint);
        Controls.Add(root);
        ResumeLayout(true);

        Shown += (_, _) => Activate();
    }
}
