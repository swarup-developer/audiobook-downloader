using AudiobookDownloader.Core;

namespace AudiobookDownloader.WinForms;

/// <summary>Asks for AAXC key/IV or AAX activation bytes when decrypting a local file.</summary>
internal sealed class KeyPromptForm : Form
{
    private readonly TextBox keyBox;
    private readonly TextBox ivBox;
    private readonly TextBox activationBox;
    private readonly Label keyLabel;
    private readonly Label ivLabel;
    private readonly Label activationLabel;

    public CachedKeys? ResultKeys { get; private set; }

    public KeyPromptForm(string fileName, bool askKeyIv, bool askActivationBytes)
    {
        Text = Strings.T("Key.Title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 240);
        AutoScaleMode = AutoScaleMode.Font;
        Strings.ApplyDirection(this);
        AccessibleName = Strings.T("Key.Title");
        AccessibleRole = AccessibleRole.Dialog;

        var fileLabel = new Label
        {
            Text = string.Format(Strings.T("Key.File"), Path.GetFileName(fileName)),
            Location = new Point(12, 12),
            AutoSize = true,
        };
        fileLabel.AccessibleName = Strings.T("Key.FileAccessible");
        Controls.Add(fileLabel);

        var explain = new Label
        {
            Text = Strings.T(askKeyIv ? "Key.ExplainKeyIv" : "Key.ExplainActivation"),
            Location = new Point(12, 38),
            AutoSize = true,
        };
        explain.AccessibleName = Strings.T("Key.ExplanationAccessible");
        Controls.Add(explain);

        keyLabel = new Label
        {
            Text = Strings.T("Key.KeyLabel"),
            Location = new Point(12, 78),
            Size = new Size(90, 23),
            TabIndex = 0,
        };
        Controls.Add(keyLabel);
        keyBox = new TextBox { Location = new Point(108, 75), Width = 380, TabIndex = 1 };
        keyBox.AccessibleName = Strings.T("Key.KeyAccessible");
        Controls.Add(keyBox);

        ivLabel = new Label
        {
            Text = Strings.T("Key.IvLabel"),
            Location = new Point(12, 110),
            Size = new Size(90, 23),
            TabIndex = 2,
        };
        Controls.Add(ivLabel);
        ivBox = new TextBox { Location = new Point(108, 107), Width = 380, TabIndex = 3 };
        ivBox.AccessibleName = Strings.T("Key.IvAccessible");
        Controls.Add(ivBox);

        activationLabel = new Label
        {
            Text = Strings.T("Key.ActivationLabel"),
            Location = new Point(12, 143),
            Size = new Size(110, 23),
            TabIndex = 4,
        };
        Controls.Add(activationLabel);
        activationBox = new TextBox { Location = new Point(128, 140), Width = 200, TabIndex = 5 };
        activationBox.AccessibleName = Strings.T("Key.ActivationAccessible");
        Controls.Add(activationBox);

        keyLabel.Visible = ivLabel.Visible = keyBox.Visible = ivBox.Visible = askKeyIv;
        activationLabel.Visible = activationBox.Visible = askActivationBytes;

        var ok = new Button
        {
            Text = Strings.T("Common.OK"),
            DialogResult = DialogResult.OK,
            Location = new Point(330, 190),
            Size = new Size(84, 30),
            TabIndex = 6,
            AccessibleName = Strings.T("Key.OkAccessible"),
            AccessibleRole = AccessibleRole.PushButton,
        };
        ok.Click += (_, _) =>
        {
            if (askKeyIv)
            {
                var key = keyBox.Text.Trim();
                var iv = ivBox.Text.Trim();
                if (key.Length != 32 || iv.Length != 32 ||
                    !key.All(Uri.IsHexDigit) || !iv.All(Uri.IsHexDigit))
                {
                    MessageBox.Show(this,
                        Strings.T("Key.BadKeyIv"),
                        Strings.T("Key.BadKeyIvTitle"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                ResultKeys = new CachedKeys(null, key, iv, null);
            }
            else
            {
                var act = activationBox.Text.Trim();
                if (act.Length != 8 || !act.All(Uri.IsHexDigit))
                {
                    MessageBox.Show(this,
                        Strings.T("Key.BadActivation"),
                        Strings.T("Key.BadActivationTitle"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                ResultKeys = new CachedKeys(null, null, null, act);
            }
        };
        Controls.Add(ok);

        var cancel = new Button
        {
            Text = Strings.T("Common.Cancel"),
            DialogResult = DialogResult.Cancel,
            Location = new Point(424, 190),
            Size = new Size(84, 30),
            TabIndex = 7,
            AccessibleName = Strings.T("Key.CancelAccessible"),
            AccessibleRole = AccessibleRole.PushButton,
        };
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }
}
