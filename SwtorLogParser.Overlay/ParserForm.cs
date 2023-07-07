using SwtorLogParser.Monitor;
using SwtorLogParser.Overlay.View;

namespace SwtorLogParser.Overlay;

using static NativeMethods;

public sealed class ParserForm : Form
{
    private readonly CombatLogsMonitor _monitor;
    private readonly SlidingExpirationList _list;
    private readonly Color _semiTransparentColor = Color.FromArgb(255, Color.White);
    private readonly IDisposable _hpsSubscription;
    private readonly IDisposable _dpsSubscription;
    
    public ParserForm(CombatLogsMonitor monitor)
    {
        _monitor = monitor;
        _list = new(this, TimeSpan.FromSeconds(10));
        _dpsSubscription = _monitor.DPS.Subscribe(OnNext);
        _hpsSubscription = _monitor.HPS.Subscribe(OnNext);
        
        DoubleBuffered = true;
        TopMost = true;
        AllowTransparency = true;
        ControlBox = true;
        AutoSize = true;
        
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(800, 450);
        
        Opacity = 0.5;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Normal;
        StartPosition = FormStartPosition.CenterScreen;
        TransparencyKey = Color.FromArgb(0, 0, 0);
        BackColor = Color.FromArgb(0, 0, 0);

        var columns = new DataGridViewTextBoxColumn[]
        {
            new() { Name = "Name", DataPropertyName = nameof(Entry.Name) },
            new() { Name = "DPS", DataPropertyName = nameof(Entry.DPS) },
            new() { Name = "Crit %", DataPropertyName = nameof(Entry.DCrit) },
            new() { Name = "HPS", DataPropertyName = nameof(Entry.HPS) },
            new() { Name = "Crit %", DataPropertyName = nameof(Entry.HCrit) }
        };

        var dataGridView = new DataGridView()
        {
            AutoGenerateColumns = false,
            BorderStyle = BorderStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            Dock = DockStyle.Fill,
            EnableHeadersVisualStyles = false,
            Enabled = true,
            ReadOnly = true,
            EditMode = DataGridViewEditMode.EditProgrammatically,
            RowHeadersVisible = false,
            Capture = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false,
            AllowUserToOrderColumns = false,
            AllowDrop = false,
            MultiSelect = false,
            DataSource = _list
        };
        
        dataGridView.Columns.AddRange(columns);
        
        dataGridView.BackgroundColor = Color.FromArgb(0, 0, 0);
        dataGridView.DefaultCellStyle.SelectionBackColor = dataGridView.DefaultCellStyle.BackColor;
        dataGridView.DefaultCellStyle.SelectionForeColor = dataGridView.DefaultCellStyle.ForeColor;
        dataGridView.MouseDown += DataGridViewOnMouseDown;
        
        Controls.Add(dataGridView);
        
        Activated -= OnActivated;
        Activated += OnActivated;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (!_monitor.IsRunning)
        {
            _monitor.Start(CancellationToken.None);
        }
    }

    private void OnNext(CombatLogsMonitor.PlayerStats stats)
    {
        _list.AddOrUpdate(stats);
    }

    private void DataGridViewOnMouseDown(object? sender, MouseEventArgs e)
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