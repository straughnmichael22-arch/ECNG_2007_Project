
using System;
using System.Drawing;
using System.Windows.Forms;

namespace EDAEnvironmentManager
{
    public class StartupWindow : Form
    {
        private Label titleLabel;
        private Label descriptionLabel;
        private Button runAuditButton;

        public StartupWindow()
        {
            Text = "Windows EDA Environment Manager";
            Size = new Size(650, 420);
            MinimumSize = new Size(550, 380);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(245, 247, 250);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(25),
                BackColor = Color.Transparent
            };

            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));

            layout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 30));

            layout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 40));

            layout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 30));

            titleLabel = new Label
            {
                Text = "EDA Environment Manager",
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 45, 60),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false
            };

            descriptionLabel = new Label
            {
                Text = "Welcome to the EDA Environment Manager.\n\n" +
                       "Click 'Run Audit' to check your system for approved EDA tools,\n" +
                       "with built-in support for remediation and automated testing.",
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.FromArgb(90, 100, 115),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false
            };

            runAuditButton = new Button
            {
                Text = "Run Audit",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Size = new Size(180, 50),
                Anchor = AnchorStyles.None,
                BackColor = Color.FromArgb(35, 110, 190),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };

            runAuditButton.FlatAppearance.BorderSize = 0;
            runAuditButton.Click += RunAuditButton_Click;

            layout.Controls.Add(titleLabel, 0, 0);
            layout.Controls.Add(descriptionLabel, 0, 1);
            layout.Controls.Add(runAuditButton, 0, 2);

            Controls.Add(layout);
        }

        private void RunAuditButton_Click(object? sender, EventArgs e)
        {
            AuditWindow auditWindow = new AuditWindow();
            auditWindow.Show();
        }
    }
}
