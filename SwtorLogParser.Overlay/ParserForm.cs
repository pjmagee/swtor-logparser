using SwtorLogParser.Monitor;
using SwtorLogParser.Overlay.View;

namespace SwtorLogParser.Overlay;

using static NativeMethods;

public sealed class ParserForm : Form
{
    private DataGridView dataGridView;
    private Button increaseButton;
    private Button decreaseButton;
    private readonly IDisposable _hpsDpsSubscription;
    
    private readonly SlidingExpirationList _list;
    private readonly CombatLogsMonitor _monitor;
    private readonly Color _semiTransparentColor = Color.FromArgb(255, Color.Black);
    

    public ParserForm(CombatLogsMonitor monitor)
    {
        _monitor = monitor;
       
        _hpsDpsSubscription = _monitor.DpsHps.Subscribe(OnNext);

        DoubleBuffered = true;
        TopMost = true;
        AllowTransparency = true;
        ControlBox = true;
        AutoSize = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Opacity = 0.5;
        Dock = DockStyle.Fill;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Normal;
        StartPosition = FormStartPosition.CenterScreen;
        TransparencyKey = Color.FromArgb(0, 0, 0);
        BackColor = Color.FromArgb(0, 0, 0);

        var columns = new DataGridViewTextBoxColumn[]
        {
            new() { Name = "Player", DataPropertyName = nameof(Entry.Name) },
            new() { Name = "DPS", DataPropertyName = nameof(Entry.DPS) },
            new() { Name = "Crit %", DataPropertyName = nameof(Entry.DCrit) },
            new() { Name = "HPS", DataPropertyName = nameof(Entry.HPS) },
            new() { Name = "Crit %", DataPropertyName = nameof(Entry.HCrit) }
        };

        dataGridView = new DataGridView
        {
            AutoSize = true,
            Enabled = true,
            ReadOnly = true,
            AutoGenerateColumns = false,
            EnableHeadersVisualStyles = false,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false,
            AllowUserToOrderColumns = false,
            AllowDrop = false,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            Anchor = AnchorStyles.Top,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            Dock = DockStyle.Fill,
            EditMode = DataGridViewEditMode.EditProgrammatically,
            ScrollBars = ScrollBars.None,
            BackgroundColor = Color.FromArgb(0, 0, 0)
        };
        
        dataGridView.DefaultCellStyle.SelectionBackColor = dataGridView.DefaultCellStyle.BackColor;
        dataGridView.DefaultCellStyle.SelectionForeColor = dataGridView.DefaultCellStyle.ForeColor;
        dataGridView.MouseDown += MouseDown;
        dataGridView.Columns.AddRange(columns);
        
        // Create the buttons
        increaseButton = new Button();
        increaseButton.BackColor = Color.Cyan;
        increaseButton.Text = "➕";
        increaseButton.Height = dataGridView.ColumnHeadersHeight;
        increaseButton.Click += IncreaseButton_Click;
        
        decreaseButton = new Button();
        decreaseButton.BackColor = Color.Cyan;
        decreaseButton.Text = "➖";
        decreaseButton.Height = dataGridView.ColumnHeadersHeight;
        decreaseButton.Click += DecreaseButton_Click;

        var layout = new FlowLayoutPanel()
        {
            AutoScroll = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Fill
        };
        
        
        
        layout.Controls.Add(increaseButton);
        layout.Controls.Add(decreaseButton);
        layout.Controls.Add(dataGridView);
      
        Controls.Add(layout);

        Activated -= OnActivated;
        Activated += OnActivated;
        
        dataGridView.DataSource = _list = new SlidingExpirationList(dataGridView, TimeSpan.FromSeconds(10));
    }

    private void DecreaseButton_Click(object? sender, EventArgs e)
    {
        dataGridView.ColumnHeadersDefaultCellStyle.Font = new Font(dataGridView.ColumnHeadersDefaultCellStyle.Font.FontFamily, dataGridView.ColumnHeadersDefaultCellStyle.Font.Size - 1);
        dataGridView.DefaultCellStyle.Font = new Font(dataGridView.DefaultCellStyle.Font.FontFamily, dataGridView.DefaultCellStyle.Font.Size - 1);
    }

    private void IncreaseButton_Click(object? sender, EventArgs e)
    {
        dataGridView.ColumnHeadersDefaultCellStyle.Font = new Font(dataGridView.ColumnHeadersDefaultCellStyle.Font.FontFamily, dataGridView.ColumnHeadersDefaultCellStyle.Font.Size + 1);
        dataGridView.DefaultCellStyle.Font = new Font(dataGridView.DefaultCellStyle.Font.FontFamily, dataGridView.DefaultCellStyle.Font.Size + 1);
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (!_monitor.IsRunning) _monitor.Start(CancellationToken.None);
    }

    private void OnNext(CombatLogsMonitor.PlayerStats stats)
    {
        _list.AddOrUpdate(stats);
    }

    private void MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCL_BUTTON_DOWN, HT_CAPTION, 0);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using (var brush = new SolidBrush(_semiTransparentColor))
        {
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }
}