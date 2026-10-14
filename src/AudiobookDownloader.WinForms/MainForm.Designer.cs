namespace AudiobookDownloader.WinForms;

partial class MainForm
{
    /// <summary>Required designer variable.</summary>
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private System.Windows.Forms.MenuStrip menuStrip;
    private System.Windows.Forms.ToolStripMenuItem menuApplication;
    private System.Windows.Forms.ToolStripMenuItem menuSettings;
    private System.Windows.Forms.ToolStripMenuItem menuRefresh;
    private System.Windows.Forms.ToolStripMenuItem menuOpenFolder;
    private System.Windows.Forms.ToolStripMenuItem menuExport;
    private System.Windows.Forms.ToolStripMenuItem menuPlay;
    private System.Windows.Forms.ToolStripMenuItem menuView;
    private System.Windows.Forms.ToolStripMenuItem menuSortTitle;
    private System.Windows.Forms.ToolStripMenuItem menuSortAuthor;
    private System.Windows.Forms.ToolStripMenuItem menuSortLength;
    private System.Windows.Forms.ToolStripMenuItem menuSortAdded;
    private System.Windows.Forms.ToolStripSeparator menuViewSeparator;
    private System.Windows.Forms.ToolStripMenuItem menuHideDownloaded;
    private System.Windows.Forms.ToolStripMenuItem menuCheckUpdates;
    private System.Windows.Forms.ToolStripSeparator menuAppSeparator;
    private System.Windows.Forms.ToolStripMenuItem menuExit;
    private System.Windows.Forms.ToolStripMenuItem menuHelp;
    private System.Windows.Forms.ToolStripMenuItem menuAbout;
    private System.Windows.Forms.ToolStripSeparator menuHelpSeparator;
    private System.Windows.Forms.ToolStripMenuItem menuShortcuts;

    // The window is laid out top to bottom in three numbered groups, so every
    // control has one obvious place:
    //   1. Books        — the download provider, search, and the book list.
    //   2. Download     — Download, Cancel, and the progress bar.
    //   3. Status       — the status line.
    //
    // Only the main job's actions are on the window. Everything else — Settings,
    // the download folder, quitting — is on a shortcut and in the application menu,
    // and a book's own actions are on the book list's context menu.
    private System.Windows.Forms.TableLayoutPanel table;

    private System.Windows.Forms.GroupBox bookGroup;
    private System.Windows.Forms.TableLayoutPanel bookLayout;
    private System.Windows.Forms.FlowLayoutPanel providerRow;
    private System.Windows.Forms.Label providerLabel;
    private System.Windows.Forms.ComboBox providerCombo;
    private System.Windows.Forms.Label providerStatus;
    private System.Windows.Forms.Button accountButton;
    private System.Windows.Forms.FlowLayoutPanel searchRow;
    private System.Windows.Forms.Label searchLabel;
    private System.Windows.Forms.ComboBox searchBox;
    private System.Windows.Forms.Button searchButton;
    private System.Windows.Forms.FlowLayoutPanel libraryHeader;
    private System.Windows.Forms.Label libraryLabel;
    private System.Windows.Forms.Label libraryHint;
    private System.Windows.Forms.ListBox libraryList;

    private System.Windows.Forms.GroupBox queueGroup;
    private System.Windows.Forms.TableLayoutPanel queueLayout;
    private System.Windows.Forms.FlowLayoutPanel buttonRow;
    private System.Windows.Forms.Button downloadButton;
    private System.Windows.Forms.Button cancelButton;
    private System.Windows.Forms.FlowLayoutPanel progressRow;
    private System.Windows.Forms.Label progressLabel;
    private FocusableProgressBar progressBar;
    private LiveRegionLabel progressStatus;

    private System.Windows.Forms.GroupBox statusGroup;
    private System.Windows.Forms.TableLayoutPanel statusLayout;
    private LiveRegionLabel statusLabel;

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();

        this.menuStrip = new System.Windows.Forms.MenuStrip();
        this.menuApplication = new System.Windows.Forms.ToolStripMenuItem();
        this.menuSettings = new System.Windows.Forms.ToolStripMenuItem();
        this.menuRefresh = new System.Windows.Forms.ToolStripMenuItem();
        this.menuOpenFolder = new System.Windows.Forms.ToolStripMenuItem();
        this.menuExport = new System.Windows.Forms.ToolStripMenuItem();
        this.menuPlay = new System.Windows.Forms.ToolStripMenuItem();
        this.menuView = new System.Windows.Forms.ToolStripMenuItem();
        this.menuSortTitle = new System.Windows.Forms.ToolStripMenuItem();
        this.menuSortAuthor = new System.Windows.Forms.ToolStripMenuItem();
        this.menuSortLength = new System.Windows.Forms.ToolStripMenuItem();
        this.menuSortAdded = new System.Windows.Forms.ToolStripMenuItem();
        this.menuViewSeparator = new System.Windows.Forms.ToolStripSeparator();
        this.menuHideDownloaded = new System.Windows.Forms.ToolStripMenuItem();
        this.menuCheckUpdates = new System.Windows.Forms.ToolStripMenuItem();
        this.menuAppSeparator = new System.Windows.Forms.ToolStripSeparator();
        this.menuExit = new System.Windows.Forms.ToolStripMenuItem();
        this.menuHelp = new System.Windows.Forms.ToolStripMenuItem();
        this.menuAbout = new System.Windows.Forms.ToolStripMenuItem();
        this.menuHelpSeparator = new System.Windows.Forms.ToolStripSeparator();
        this.menuShortcuts = new System.Windows.Forms.ToolStripMenuItem();

        this.table = new System.Windows.Forms.TableLayoutPanel();

        this.bookGroup = new System.Windows.Forms.GroupBox();
        this.bookLayout = new System.Windows.Forms.TableLayoutPanel();
        this.providerRow = new System.Windows.Forms.FlowLayoutPanel();
        this.providerLabel = new System.Windows.Forms.Label();
        this.providerCombo = new System.Windows.Forms.ComboBox();
        this.providerStatus = new System.Windows.Forms.Label();
        this.accountButton = new System.Windows.Forms.Button();
        this.searchRow = new System.Windows.Forms.FlowLayoutPanel();
        this.searchLabel = new System.Windows.Forms.Label();
        this.searchBox = new System.Windows.Forms.ComboBox();
        this.searchButton = new System.Windows.Forms.Button();
        this.libraryHeader = new System.Windows.Forms.FlowLayoutPanel();
        this.libraryLabel = new System.Windows.Forms.Label();
        this.libraryHint = new System.Windows.Forms.Label();
        this.libraryList = new System.Windows.Forms.ListBox();

        this.queueGroup = new System.Windows.Forms.GroupBox();
        this.queueLayout = new System.Windows.Forms.TableLayoutPanel();
        this.buttonRow = new System.Windows.Forms.FlowLayoutPanel();
        this.downloadButton = new System.Windows.Forms.Button();
        this.cancelButton = new System.Windows.Forms.Button();
        this.progressRow = new System.Windows.Forms.FlowLayoutPanel();
        this.progressLabel = new System.Windows.Forms.Label();
        this.progressBar = new FocusableProgressBar();
        this.progressStatus = new LiveRegionLabel();

        this.statusGroup = new System.Windows.Forms.GroupBox();
        this.statusLayout = new System.Windows.Forms.TableLayoutPanel();
        this.statusLabel = new LiveRegionLabel();

        this.menuStrip.SuspendLayout();
        this.table.SuspendLayout();
        this.bookGroup.SuspendLayout();
        this.bookLayout.SuspendLayout();
        this.providerRow.SuspendLayout();
        this.searchRow.SuspendLayout();
        this.libraryHeader.SuspendLayout();
        this.queueGroup.SuspendLayout();
        this.queueLayout.SuspendLayout();
        this.buttonRow.SuspendLayout();
        this.progressRow.SuspendLayout();
        this.statusGroup.SuspendLayout();
        this.statusLayout.SuspendLayout();
        this.SuspendLayout();

        //
        // menuStrip — the Alt application menu: Settings, About and Quit
        //
        this.menuStrip.Name = "menuStrip";
        this.menuStrip.Dock = System.Windows.Forms.DockStyle.Top;
        this.menuStrip.TabIndex = 0;
        this.menuStrip.AccessibleName = "Application menu";
        this.menuStrip.AccessibleRole = System.Windows.Forms.AccessibleRole.MenuBar;
        this.menuStrip.Items.AddRange([this.menuApplication, this.menuView, this.menuHelp]);

        //
        // menuApplication
        //
        this.menuApplication.Name = "menuApplication";
        this.menuApplication.AccessibleName = "Application";
        this.menuApplication.DropDownItems.AddRange(
        [
            this.menuPlay,
            this.menuSettings,
            this.menuRefresh,
            this.menuOpenFolder,
            this.menuExport,
            this.menuAppSeparator,
            this.menuExit,
        ]);

        //
        // menuSettings
        //
        this.menuSettings.Name = "menuSettings";
        this.menuSettings.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Oemcomma;
        this.menuSettings.ShowShortcutKeys = true;
        this.menuSettings.AccessibleName = "Settings";

        //
        // menuRefresh
        //
        this.menuRefresh.Name = "menuRefresh";
        this.menuRefresh.ShortcutKeys = System.Windows.Forms.Keys.F5;
        this.menuRefresh.ShowShortcutKeys = true;
        this.menuRefresh.AccessibleName = "Reload list";

        //
        // menuOpenFolder — the download folder used to be a button on the window
        //
        this.menuOpenFolder.Name = "menuOpenFolder";
        this.menuOpenFolder.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O;
        this.menuOpenFolder.ShowShortcutKeys = true;
        this.menuOpenFolder.AccessibleName = "Open download folder";

        //
        // menuPlay — the built-in player, for the selected book
        //
        this.menuPlay.Name = "menuPlay";
        this.menuPlay.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.P;
        this.menuPlay.ShowShortcutKeys = true;
        this.menuPlay.AccessibleName = "Play selected book";

        //
        // menuView — how the book list is sorted, and whether downloaded books show
        //
        this.menuView.Name = "menuView";
        this.menuView.AccessibleName = "View";
        this.menuView.DropDownItems.AddRange(
        [
            this.menuSortTitle,
            this.menuSortAuthor,
            this.menuSortLength,
            this.menuSortAdded,
            this.menuViewSeparator,
            this.menuHideDownloaded,
        ]);
        this.menuSortTitle.Name = "menuSortTitle";
        this.menuSortTitle.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.D1;
        this.menuSortTitle.AccessibleName = "Sort by title";
        this.menuSortAuthor.Name = "menuSortAuthor";
        this.menuSortAuthor.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.D2;
        this.menuSortAuthor.AccessibleName = "Sort by author";
        this.menuSortLength.Name = "menuSortLength";
        this.menuSortLength.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.D3;
        this.menuSortLength.AccessibleName = "Sort by length";
        this.menuSortAdded.Name = "menuSortAdded";
        this.menuSortAdded.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.D4;
        this.menuSortAdded.AccessibleName = "Sort by date added";
        this.menuHideDownloaded.Name = "menuHideDownloaded";
        this.menuHideDownloaded.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.H;
        this.menuHideDownloaded.AccessibleName = "Hide books already downloaded";

        //
        // menuCheckUpdates
        //
        this.menuCheckUpdates.Name = "menuCheckUpdates";
        this.menuCheckUpdates.AccessibleName = "Check for updates";

        //
        // menuExport — save the book list as a CSV file (opens in Excel or any editor)
        //
        this.menuExport.Name = "menuExport";
        this.menuExport.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.E;
        this.menuExport.ShowShortcutKeys = true;
        this.menuExport.AccessibleName = "Export book list";

        //
        // menuExit
        //
        this.menuExit.Name = "menuExit";
        this.menuExit.ShortcutKeys = System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.F4;
        this.menuExit.ShowShortcutKeys = true;
        this.menuExit.AccessibleName = "Quit";

        //
        // menuHelp
        //
        this.menuHelp.Name = "menuHelp";
        this.menuHelp.AccessibleName = "Help";
        this.menuHelp.DropDownItems.AddRange(
        [
            this.menuShortcuts,
            this.menuHelpSeparator,
            this.menuCheckUpdates,
            this.menuAbout,
        ]);

        //
        // menuAbout
        //
        this.menuAbout.Name = "menuAbout";
        this.menuAbout.AccessibleName = "About";

        //
        // menuShortcuts
        //
        this.menuShortcuts.Name = "menuShortcuts";
        this.menuShortcuts.ShortcutKeys = System.Windows.Forms.Keys.F1;
        this.menuShortcuts.ShowShortcutKeys = true;
        this.menuShortcuts.AccessibleName = "Keyboard shortcuts";

        //
        // table — three groups in reading order, one per row
        //
        this.table.Name = "table";
        this.table.ColumnCount = 1;
        this.table.RowCount = 3;
        this.table.Dock = System.Windows.Forms.DockStyle.Fill;
        this.table.Padding = new System.Windows.Forms.Padding(8);
        this.table.TabIndex = 1;
        this.table.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F)); // books
        this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));     // download
        this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));     // status
        this.table.Controls.Add(this.bookGroup, 0, 0);
        this.table.Controls.Add(this.queueGroup, 0, 1);
        this.table.Controls.Add(this.statusGroup, 0, 2);

        //
        // bookGroup — 1. Books
        //
        this.bookGroup.Dock = System.Windows.Forms.DockStyle.Fill;
        this.bookGroup.Padding = new System.Windows.Forms.Padding(8, 2, 8, 6);
        this.bookGroup.Text = "1. Books";
        this.bookGroup.Controls.Add(this.bookLayout);

        //
        // bookLayout — provider, search, book list, in order
        //
        this.bookLayout.ColumnCount = 1;
        this.bookLayout.RowCount = 4;
        this.bookLayout.Dock = System.Windows.Forms.DockStyle.Fill;
        this.bookLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        this.bookLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        this.bookLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        this.bookLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        this.bookLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        this.bookLayout.Controls.Add(this.providerRow, 0, 0);
        this.bookLayout.Controls.Add(this.searchRow, 0, 1);
        this.bookLayout.Controls.Add(this.libraryHeader, 0, 2);
        this.bookLayout.Controls.Add(this.libraryList, 0, 3);

        //
        // providerRow
        //
        this.providerRow.Name = "providerRow";
        this.providerRow.Dock = System.Windows.Forms.DockStyle.Fill;
        this.providerRow.AutoSize = true;
        this.providerRow.WrapContents = true;
        this.providerRow.Controls.Add(this.providerLabel);
        this.providerRow.Controls.Add(this.providerCombo);
        this.providerRow.Controls.Add(this.providerStatus);
        this.providerRow.Controls.Add(this.accountButton);

        //
        // providerLabel
        //
        this.providerLabel.AutoSize = true;
        this.providerLabel.Text = "&Provider:";
        this.providerLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
        this.providerLabel.TabIndex = 0;

        //
        // providerCombo — the provider chooser (ComboBox)
        //
        this.providerCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.providerCombo.Width = 300;
        this.providerCombo.TabIndex = 1;
        this.providerCombo.Name = "providerCombo";
        this.providerCombo.Margin = new System.Windows.Forms.Padding(3, 3, 3, 3);
        this.providerCombo.AccessibleRole = System.Windows.Forms.AccessibleRole.ComboBox;
        this.providerCombo.SelectedIndexChanged += providerCombo_SelectedIndexChanged;

        //
        // providerStatus
        //
        // The hint colours are applied in ApplyTheme, so they can follow Windows
        // high contrast instead of being fixed here.
        this.providerStatus.AutoSize = true;
        this.providerStatus.Margin = new System.Windows.Forms.Padding(12, 8, 3, 3);
        this.providerStatus.Text = "";
        this.providerStatus.AccessibleRole = System.Windows.Forms.AccessibleRole.StaticText;

        //
        // accountButton — sign in / account management (Audible lives in Settings)
        //
        this.accountButton.AutoSize = true;
        this.accountButton.TabIndex = 2;
        this.accountButton.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
        this.accountButton.AccessibleRole = System.Windows.Forms.AccessibleRole.PushButton;
        this.accountButton.Click += accountButton_Click;

        //
        // searchRow
        //
        this.searchRow.Name = "searchRow";
        this.searchRow.Dock = System.Windows.Forms.DockStyle.Fill;
        this.searchRow.AutoSize = true;
        this.searchRow.WrapContents = true;
        this.searchRow.Controls.Add(this.searchLabel);
        this.searchRow.Controls.Add(this.searchBox);
        this.searchRow.Controls.Add(this.searchButton);

        //
        // searchLabel
        //
        this.searchLabel.AutoSize = true;
        this.searchLabel.Text = "&Search books:";
        this.searchLabel.Margin = new System.Windows.Forms.Padding(3, 9, 3, 3);
        this.searchLabel.TabIndex = 3;

        //
        // searchBox — editable combo box: type to search, or open the dropdown of
        // previous searches for the current provider (Alt+Down / F4).
        //
        this.searchBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown;
        this.searchBox.Width = 280;
        this.searchBox.TabIndex = 4;
        this.searchBox.Name = "searchBox";
        this.searchBox.Margin = new System.Windows.Forms.Padding(3, 4, 3, 3);
        this.searchBox.AccessibleRole = System.Windows.Forms.AccessibleRole.ComboBox;
        this.searchBox.KeyDown += searchBox_KeyDown;

        //
        // searchButton
        //
        this.searchButton.Text = "S&earch";
        this.searchButton.AutoSize = true;
        this.searchButton.TabIndex = 5;
        this.searchButton.AccessibleRole = System.Windows.Forms.AccessibleRole.PushButton;
        this.searchButton.Margin = new System.Windows.Forms.Padding(6, 3, 3, 3);
        this.searchButton.Click += searchButton_Click;

        //
        // libraryHeader — the caption and the inline hint for the book list
        //
        this.libraryHeader.Name = "libraryHeader";
        this.libraryHeader.Dock = System.Windows.Forms.DockStyle.Fill;
        this.libraryHeader.AutoSize = true;
        this.libraryHeader.WrapContents = true;
        this.libraryHeader.Controls.Add(this.libraryLabel);
        this.libraryHeader.Controls.Add(this.libraryHint);

        //
        // libraryLabel
        //
        this.libraryLabel.AutoSize = true;
        this.libraryLabel.Text = "Your &books:";
        this.libraryLabel.Margin = new System.Windows.Forms.Padding(3, 9, 3, 0);
        this.libraryLabel.TabIndex = 8;

        //
        // libraryHint — explains an empty list (nothing found, or not signed in)
        //
        this.libraryHint.AutoSize = true;
        this.libraryHint.Text = "";
        this.libraryHint.Margin = new System.Windows.Forms.Padding(12, 9, 3, 0);
        this.libraryHint.AccessibleRole = System.Windows.Forms.AccessibleRole.StaticText;

        //
        // libraryList
        //
        this.libraryList.Dock = System.Windows.Forms.DockStyle.Fill;
        this.libraryList.TabIndex = 9;
        this.libraryList.Name = "libraryList";
        this.libraryList.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
        this.libraryList.HorizontalScrollbar = true;
        this.libraryList.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
        this.libraryList.AccessibleRole = System.Windows.Forms.AccessibleRole.List;
        this.libraryList.DoubleClick += libraryList_DoubleClick;
        this.libraryList.KeyDown += libraryList_KeyDown;

        //
        // queueGroup — 2. Download
        //
        this.queueGroup.Dock = System.Windows.Forms.DockStyle.Fill;
        this.queueGroup.AutoSize = true;
        this.queueGroup.Padding = new System.Windows.Forms.Padding(8, 2, 8, 6);
        this.queueGroup.Text = "2. Downloads";
        this.queueGroup.Controls.Add(this.queueLayout);

        //
        // queueLayout — actions, then progress
        //
        this.queueLayout.ColumnCount = 1;
        this.queueLayout.RowCount = 2;
        this.queueLayout.Dock = System.Windows.Forms.DockStyle.Fill;
        this.queueLayout.AutoSize = true;
        this.queueLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        this.queueLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        this.queueLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        this.queueLayout.Controls.Add(this.buttonRow, 0, 0);
        this.queueLayout.Controls.Add(this.progressRow, 0, 1);

        //
        // buttonRow — the two actions the main job needs, and nothing else. Every
        // other action is on a shortcut, in the application menu or on the book
        // list's context menu.
        //
        this.buttonRow.Name = "buttonRow";
        this.buttonRow.Dock = System.Windows.Forms.DockStyle.Fill;
        this.buttonRow.AutoSize = true;
        this.buttonRow.WrapContents = true;
        this.buttonRow.Controls.Add(this.downloadButton);
        this.buttonRow.Controls.Add(this.cancelButton);

        //
        // downloadButton
        //
        this.downloadButton.Text = "&Download";
        this.downloadButton.AutoSize = true;
        this.downloadButton.TabIndex = 10;
        this.downloadButton.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
        this.downloadButton.AccessibleRole = System.Windows.Forms.AccessibleRole.PushButton;
        this.downloadButton.Click += downloadButton_Click;

        //
        // cancelButton
        //
        this.cancelButton.Text = "&Cancel download";
        this.cancelButton.AutoSize = true;
        this.cancelButton.TabIndex = 11;
        this.cancelButton.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
        this.cancelButton.AccessibleRole = System.Windows.Forms.AccessibleRole.PushButton;
        this.cancelButton.Enabled = false;
        this.cancelButton.Click += cancelButton_Click;

        //
        // progressRow — the active download's progress, exposed as a progress bar
        //
        this.progressRow.Name = "progressRow";
        this.progressRow.Dock = System.Windows.Forms.DockStyle.Fill;
        this.progressRow.AutoSize = true;
        this.progressRow.Controls.Add(this.progressLabel);
        this.progressRow.Controls.Add(this.progressBar);
        this.progressRow.Controls.Add(this.progressStatus);

        //
        // progressLabel
        //
        this.progressLabel.AutoSize = true;
        this.progressLabel.Margin = new System.Windows.Forms.Padding(3, 8, 3, 3);
        this.progressLabel.AccessibleRole = System.Windows.Forms.AccessibleRole.StaticText;
        this.progressLabel.TabStop = false;

        //
        // progressBar
        //
        this.progressBar.Minimum = 0;
        this.progressBar.Maximum = 100;
        this.progressBar.Value = 0;
        this.progressBar.Width = 300;
        this.progressBar.Height = 18;
        this.progressBar.Margin = new System.Windows.Forms.Padding(3, 5, 3, 3);
        this.progressBar.TabIndex = 12;
        this.progressBar.Name = "progressBar";
        this.progressBar.AccessibleRole = System.Windows.Forms.AccessibleRole.ProgressBar;
        this.progressBar.AccessibleName = "Download progress";

        //
        // progressStatus — its own live region, so progress announcements never
        // overwrite the last status message
        //
        this.progressStatus.AutoSize = true;
        this.progressStatus.Text = "";
        this.progressStatus.Margin = new System.Windows.Forms.Padding(12, 8, 3, 3);
        this.progressStatus.TabStop = false;
        this.progressStatus.AccessibleRole = System.Windows.Forms.AccessibleRole.StaticText;

        //
        // statusGroup — 3. Status
        //
        this.statusGroup.Dock = System.Windows.Forms.DockStyle.Fill;
        this.statusGroup.AutoSize = true;
        this.statusGroup.Padding = new System.Windows.Forms.Padding(8, 2, 8, 6);
        this.statusGroup.Text = "3. Status";
        this.statusGroup.Controls.Add(this.statusLayout);

        //
        // statusLayout — the status line
        //
        this.statusLayout.Name = "statusLayout";
        this.statusLayout.ColumnCount = 1;
        this.statusLayout.RowCount = 1;
        this.statusLayout.Dock = System.Windows.Forms.DockStyle.Fill;
        this.statusLayout.AutoSize = true;
        this.statusLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        this.statusLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        this.statusLayout.Controls.Add(this.statusLabel, 0, 0);

        //
        // statusLabel — a live region: NVDA reads it whenever the text changes
        //
        this.statusLabel.AutoSize = true;
        this.statusLabel.Text = "Ready.";
        this.statusLabel.Margin = new System.Windows.Forms.Padding(3, 4, 3, 3);
        this.statusLabel.TabIndex = 14;
        this.statusLabel.TabStop = false;
        this.statusLabel.AccessibleName = "Status";
        this.statusLabel.AccessibleRole = System.Windows.Forms.AccessibleRole.StaticText;

        //
        // MainForm
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(900, 820);
        this.MinimumSize = new System.Drawing.Size(840, 700);
        this.Controls.Add(this.table);
        this.Controls.Add(this.menuStrip);
        this.MainMenuStrip = this.menuStrip;
        this.Text = "Audiobook Downloader";
        this.KeyPreview = true;
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.KeyDown += MainForm_KeyDown;
        this.HelpRequested += MainForm_HelpRequested;

        this.menuStrip.ResumeLayout(false);
        this.table.ResumeLayout(false);
        this.bookGroup.ResumeLayout(false);
        this.bookLayout.ResumeLayout(false);
        this.bookLayout.PerformLayout();
        this.providerRow.ResumeLayout(false);
        this.providerRow.PerformLayout();
        this.searchRow.ResumeLayout(false);
        this.searchRow.PerformLayout();
        this.libraryHeader.ResumeLayout(false);
        this.libraryHeader.PerformLayout();
        this.queueGroup.ResumeLayout(false);
        this.queueLayout.ResumeLayout(false);
        this.queueLayout.PerformLayout();
        this.buttonRow.ResumeLayout(false);
        this.buttonRow.PerformLayout();
        this.progressRow.ResumeLayout(false);
        this.progressRow.PerformLayout();
        this.statusGroup.ResumeLayout(false);
        this.statusLayout.ResumeLayout(false);
        this.statusLayout.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
