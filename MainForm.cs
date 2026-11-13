using System.Diagnostics;
using System.Globalization;

namespace VigorSearch;

public class MainForm : Form
{
    const string AnyText = "(指定なし)";

    readonly TextBox[] stateBoxes = new TextBox[4];
    readonly NumericUpDown nudListCount = new() { Minimum = 1, Maximum = 5000, Value = 100, Width = 80 };
    readonly NumericUpDown nudStart = new() { Minimum = 0, Maximum = 10_000_000, Value = 0, Width = 120, ThousandsSeparator = true };
    readonly ComboBox cbSkill1 = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDown };
    readonly ComboBox cbSkill2 = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDown };
    readonly ComboBox cbSlot = new() { Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown nudLv1 = new() { Minimum = 1, Maximum = 7, Value = 1, Width = 45 };
    readonly NumericUpDown nudLv2 = new() { Minimum = 1, Maximum = 7, Value = 1, Width = 45 };
    readonly NumericUpDown nudRange = new() { Minimum = 1, Maximum = 10_000_000, Value = 10_000, Increment = 1, Width = 120, ThousandsSeparator = true };
    readonly NumericUpDown nudMaxHit = new() { Minimum = 1, Maximum = 10_000, Value = 50, Width = 90 };
    readonly CheckBox chkAnyOrder = new() { Text = "スキル1/2の順不同", Checked = false, AutoSize = true, Margin = new Padding(8, 5, 3, 3) };

    readonly Button btnList = new() { Text = "一覧表示", AutoSize = true };
    readonly Button btnSearch = new() { Text = "検索", AutoSize = true };
    readonly Button btnCancel = new() { Text = "中止", AutoSize = true, Enabled = false };
    readonly ProgressBar bar = new() { Width = 160, Height = 18, Margin = new Padding(12, 6, 3, 3) };
    readonly Label status = new() { AutoSize = true, Margin = new Padding(8, 7, 3, 3) };
    readonly DataGridView grid = new();

    readonly TextBox txtObs = new() { Width = 120, Text = "obs.txt" };
    readonly Button btnRecover = new() { Text = "Stateを復元", AutoSize = true };

    CancellationTokenSource cts;

    public MainForm()
    {
        Text = "VigorSearch";
        Width = 960;
        Height = 580;
        StartPosition = FormStartPosition.CenterScreen;   // 起動時に画面中央へ
        Font = new Font("Yu Gothic UI", 9f);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        // 初期State
        var defaults = new[] { "12345678", "23456789", "3456789A", "456789AB" };
        var stateRow = NewFlow();
        for (int i = 0; i < 4; i++)
        {
            stateBoxes[i] = new TextBox { Width = 100, MaxLength = 10, Text = defaults[i], Font = new Font("Consolas", 10f) };
            stateRow.Controls.Add(Lbl($"S{i}"));
            stateRow.Controls.Add(stateBoxes[i]);
        }
        stateRow.Controls.Add(Lbl("開始"));      
        stateRow.Controls.Add(nudStart);         
        stateRow.Controls.Add(Lbl("件数"));
        stateRow.Controls.Add(nudListCount);
        stateRow.Controls.Add(btnList);
        // 検索条件
        var skillRow = NewFlow();
        skillRow.Controls.AddRange(new Control[]
        {
            Lbl("スキル1"), cbSkill1, Lbl("Lv≧"), nudLv1,
            Lbl("スキル2"), cbSkill2, Lbl("Lv≧"), nudLv2,
            Lbl("スロット"), cbSlot, chkAnyOrder,
        });
        var runRow = NewFlow();
        runRow.Controls.AddRange(new Control[]
        {
            Lbl("探索範囲(件)"), nudRange, Lbl("最大ヒット数"), nudMaxHit,
            btnSearch, btnCancel, bar,
        });
        var searchPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        searchPanel.Controls.Add(skillRow);
        searchPanel.Controls.Add(runRow);
        var obsRow = NewFlow();
        obsRow.Controls.Add(Lbl("観測ファイル"));
        obsRow.Controls.Add(txtObs);
        obsRow.Controls.Add(btnRecover);

        var statePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        statePanel.Controls.Add(stateRow);
        statePanel.Controls.Add(obsRow);

        var gbState = Group("初期State(16進)・一覧出力", statePanel);
        var gbSearch = Group("検索", searchPanel);

        // コンボボックスの初期化
        var names = new List<string> { AnyText };
        names.AddRange(SkillData.AllSkillNames());
        foreach (var cb in new[] { cbSkill1, cbSkill2 })
        {
            cb.Items.AddRange(names.ToArray());
            cb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            cb.AutoCompleteSource = AutoCompleteSource.ListItems;
            cb.SelectedIndex = 0;
        }
        cbSlot.Items.Add(AnyText);
        cbSlot.Items.AddRange(SkillData.Slots.Select(x => (object)x.name).ToArray());
        cbSlot.SelectedIndex = 0;

        // グリッド
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;    
        grid.RowHeadersVisible = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

       
        typeof(DataGridView)
            .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(grid, true);
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithAutoHeaderText;
        grid.CellDoubleClick += OnGridDoubleClick;

        // 下部ステータス
        var hint = new Label
        {
            Text = "State列をダブルクリックするとそのStateを初期Stateに設定します",
            AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 7, 3, 3),
        };
        var bottom = NewFlow();
        bottom.Controls.Add(status);
        bottom.Controls.Add(hint);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(gbState, 0, 0);
        layout.Controls.Add(gbSearch, 0, 1);
        layout.Controls.Add(grid, 0, 2);
        layout.Controls.Add(bottom, 0, 3);
        Controls.Add(layout);

        btnList.Click += OnList;
        btnSearch.Click += OnSearch;
        btnCancel.Click += (_, _) => cts?.Cancel();
        btnRecover.Click += OnRecover;
        AcceptButton = btnSearch;
       
    }

    // ---------- UI helpers ----------
    static FlowLayoutPanel NewFlow() => new() { AutoSize = true, WrapContents = false };

    static Label Lbl(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(10, 7, 3, 3) };

    static GroupBox Group(string title, Control inner)
    {
        var gb = new GroupBox { Text = title, Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 4, 6, 6) };
        inner.Dock = DockStyle.Top;
        gb.Controls.Add(inner);
        return gb;
    }

    void SetBusy(bool busy)
    {
        btnSearch.Enabled = !busy;
        btnList.Enabled = !busy;
        btnCancel.Enabled = busy;
        if (!busy) bar.Value = 0;
    }

    void ShowRows(List<Row> rows)
    {
        grid.DataSource = rows;
        foreach (var name in new[] { nameof(Row.StateBefore) })
        {
            if (grid.Columns[name] is DataGridViewColumn c)
                c.DefaultCellStyle.Font = new Font("Consolas", 9.5f);
        }

        grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
    }
    // ---------- 入力の取得 ----------
    bool TryGetState(out XorShiftState st)
    {
        st = default;
        var v = new uint[4];
        for (int i = 0; i < 4; i++)
        {
            var t = stateBoxes[i].Text.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
            if (!uint.TryParse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out v[i]))
            {
                MessageBox.Show($"S{i} は16進数(最大8桁)で入力してください。", "入力エラー");
                stateBoxes[i].Focus();
                return false;
            }
        }
        st = new XorShiftState(v[0], v[1], v[2], v[3]);
        return true;
    }

    bool TryGetSkillFilter(ComboBox cb, out string name)
    {
        name = null;
        var t = cb.Text.Trim();
        if (t.Length == 0 || t == AnyText) return true;
        if (!SkillData.AllSkillNames().Contains(t))
        {
            MessageBox.Show($"「{t}」というスキルは見つかりません。一覧から選んでください。", "入力エラー");
            cb.Focus();
            return false;
        }
        name = t;
        return true;
    }

    void OnList(object sender, EventArgs e)
    {
        if (!TryGetState(out var st)) return;
        int start = (int)nudStart.Value;
        int n = (int)nudListCount.Value;

        // 開始位置まで読み飛ばす(1件 = Next() 9回)
        for (long k = 0; k < 9L * start; k++) st.Next();

        var rows = new List<Row>(n);
        for (int j = 0; j < n; j++) rows.Add(Generator.Generate(ref st, start + j));
        ShowRows(rows);
        status.Text = $"#{start} から {n}件を出力しました";
    }

    // ---------- 検索 ----------
    async void OnSearch(object sender, EventArgs e)
    {
        if (!TryGetState(out var st)) return;
        if (!TryGetSkillFilter(cbSkill1, out var w1) || !TryGetSkillFilter(cbSkill2, out var w2)) return;

        string slot = cbSlot.SelectedIndex <= 0 ? null : cbSlot.Text;
        int lv1 = (int)nudLv1.Value, lv2 = (int)nudLv2.Value;
        int range = (int)nudRange.Value, maxHit = (int)nudMaxHit.Value;
        bool anyOrder = chkAnyOrder.Checked;

        SetBusy(true);
        status.Text = "検索中...";
        cts = new CancellationTokenSource();
        var token = cts.Token;
        var progress = new Progress<int>(p => bar.Value = Math.Clamp(p, 0, 100));
        var sw = Stopwatch.StartNew();

        try
        {
            var (hits, scanned) = await Task.Run(() =>
                Search(st, range, maxHit, w1, lv1, w2, lv2, slot, anyOrder, token, progress));

            ShowRows(hits);
            var msg = $"{hits.Count}件ヒット / {scanned:N0}件を探索 ({sw.Elapsed.TotalSeconds:F1}秒)";
            if (token.IsCancellationRequested) msg += " ※中止しました";
            else if (hits.Count >= maxHit) msg += " ※最大ヒット数で打ち切り";
            status.Text = msg;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "検索エラー");
            status.Text = "エラー";
        }
        finally
        {
            SetBusy(false);
        }
    }

    static bool Match(string name, int lv, string want, int minLv)
        => (want == null || name == want) && lv >= minLv;

    static (List<Row> hits, int scanned) Search(
        XorShiftState st, int range, int maxHit,
        string w1, int lv1, string w2, int lv2, string slot, bool anyOrder,
        CancellationToken ct, IProgress<int> progress)
    {
        var hits = new List<Row>();
        int j = 0;
        for (; j < range; j++)
        {
            if ((j & 0x3FFF) == 0)
            {
                if (ct.IsCancellationRequested) break;
                progress.Report((int)(100L * j / range));
            }

            var r = Generator.Generate(ref st, j);
            if (slot != null && r.Slot != slot) continue;

            bool ok =
                (Match(r.Skill1, r.Lv1, w1, lv1) && Match(r.Skill2, r.Lv2, w2, lv2)) ||
                (anyOrder && Match(r.Skill1, r.Lv1, w2, lv2) && Match(r.Skill2, r.Lv2, w1, lv1));
            if (!ok) continue;

            hits.Add(r);
            if (hits.Count >= maxHit) { j++; break; }
        }
        return (hits, j);
    }

    // ---------- ダブルクリックで開始Stateに設定 ----------
    
    void OnGridDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        if (grid.Columns[e.ColumnIndex].DataPropertyName != nameof(Row.StateBefore)) return;

        if (grid.Rows[e.RowIndex].DataBoundItem is not Row r) return;

        SetState(r.Before);
        status.Text = $"初期Stateを # {r.Index} の開始Stateに設定しました";
    }
    // ---------- 観測ファイルから初期Stateを復元 ----------
    void SetState(XorShiftState s)
    {
        stateBoxes[0].Text = s.S0.ToString("X8");
        stateBoxes[1].Text = s.S1.ToString("X8");
        stateBoxes[2].Text = s.S2.ToString("X8");
        stateBoxes[3].Text = s.S3.ToString("X8");
    }

    async void OnRecover(object sender, EventArgs e)
    {
        var name = txtObs.Text.Trim();
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, name),          // exeと同じフォルダ
            Path.Combine(Directory.GetCurrentDirectory(), name),   // カレントフォルダ
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path == null)
        {
            MessageBox.Show("ファイルが見つかりません。\n" + string.Join("\n", candidates), "復元");
            return;
        }

        btnRecover.Enabled = false;
        status.Text = "復元中...";
        try
        {
            var res = await Task.Run(() => StateRecovery.Recover(path));
            if (res.Success)
            {
                SetState(res.State);   
                status.Text = "復元完了: " + res.Log.Trim().Replace("\r\n", " / ").Replace("\n", " / ");
            }
            else
            {
                status.Text = "復元に失敗";
                MessageBox.Show(res.Log, "復元");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "復元エラー");
        }
        finally
        {
            btnRecover.Enabled = true;
        }
    }
}
