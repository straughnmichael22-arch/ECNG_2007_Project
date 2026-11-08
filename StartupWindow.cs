
using System;
using System.Drawing;
using System.Windows.Forms;

namespace EDAEnvironmentAudit
{
    public class StartupWindow : Form
    {
        private Label titleLabel;
        private Label descriptionLabel;
        private Button runAuditButton;

        public StartupWindow()
        {
            // Window configuration
            Text = "Windows EDA Tool Environment Audit";
            Size = new Size(650, 420);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            BackColor = Color.FromArgb(245, 247, 250);

            // Application title
            titleLabel = new Label();
            titleLabel.Text = "EDA Environment Audit";
            titleLabel.Font = new Font("Segoe UI", 22, FontStyle.Bold);
            titleLabel.ForeColor = Color.FromArgb(35, 45, 60);
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(125, 85);

            // Description
            descriptionLabel = new Label();
            descriptionLabel.Text =
                  "Welcome to Course Tool Audit.\n" +
    "Click Run Audit to check for Vivado, VHDL extension, Visual Studio Code,\n" + 
    "GHDL and Surfer";

            descriptionLabel.Font = new Font("Segoe UI", 11);
            descriptionLabel.ForeColor = Color.FromArgb(90, 100, 115);
            descriptionLabel.AutoSize = true;
            descriptionLabel.Location = new Point(65, 160);
            descriptionLabel.TextAlign = ContentAlignment.MiddleCenter;

            // Run Audit button
            runAuditButton = new Button();
            runAuditButton.Text = "Run Audit";
            runAuditButton.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            runAuditButton.Size = new Size(180, 50);
            runAuditButton.Location = new Point(225, 260);
            runAuditButton.BackColor = Color.FromArgb(35, 110, 190);
            runAuditButton.ForeColor = Color.White;
            runAuditButton.FlatStyle = FlatStyle.Flat;
            runAuditButton.FlatAppearance.BorderSize = 0;
            runAuditButton.Cursor = Cursors.Hand;

            runAuditButton.Click += RunAuditButton_Click;

            // Add controls to window
            Controls.Add(titleLabel);
            Controls.Add(descriptionLabel);
            Controls.Add(runAuditButton);
        }

        private void RunAuditButton_Click(object? sender, EventArgs e)
        {
            AuditWindow auditWindow = new AuditWindow();
            auditWindow.Show();
        }
    }
}
