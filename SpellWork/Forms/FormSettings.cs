using SpellWork.Database;
using SpellWork.Properties;
using System;
using System.IO;
using System.Windows.Forms;

namespace SpellWork.Forms
{
    public partial class FormSettings : Form
    {
        public FormSettings()
        {
            InitializeComponent();
        }

        private void CbUseDbConnectCheckedChanged(object sender, EventArgs e)
        {
            _gbDbSetting.Enabled = ((CheckBox)sender).Checked;
            _cbReadOnlyDB.Enabled = _gbDbSetting.Enabled;
            _bTestConnect.Enabled = _gbDbSetting.Enabled;
        }

        private void BSaveSettingsClick(object sender, EventArgs e)
        {
            Settings.Default.Host = _tbHost.Text;
            Settings.Default.PortOrPipe = _tbPort.Text;
            Settings.Default.User = _tbUser.Text;
            Settings.Default.Pass = _tbPass.Text;
            Settings.Default.WorldDbName = _tbBase.Text;
            Settings.Default.UseDbConnect = _cbUseDB.Checked;
            Settings.Default.DbIsReadOnly = _cbReadOnlyDB.Checked;
            Settings.Default.DbcPath = _tbPath.Text;
            Settings.Default.GtPath = _tbGtPath.Text;
            Settings.Default.Locale = _tbLocale.Text;
            Settings.Default.RepoPath = _tbRepoPath.Text;

            MySqlConnection.TestConnect();

            if (((Button)sender).Text != @"Save")
                if (MySqlConnection.Connected)
                    MessageBox.Show(@"Connection successful!", @"MySQL Connections!", MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (((Button)sender).Text != @"Save")
                return;

            Settings.Default.Save();
            Close();

            Application.Restart();
        }

        private void SettingsFormLoad(object sender, EventArgs e)
        {
            _tbHost.Text = Settings.Default.Host;
            _tbPort.Text = Settings.Default.PortOrPipe;
            _tbUser.Text = Settings.Default.User;
            _tbPass.Text = Settings.Default.Pass;
            _tbBase.Text = Settings.Default.WorldDbName;
            _gbDbSetting.Enabled = _cbUseDB.Checked = Settings.Default.UseDbConnect;
            _cbReadOnlyDB.Checked = Settings.Default.DbIsReadOnly;
            _tbPath.Text = Settings.Default.DbcPath;
            _tbGtPath.Text = Settings.Default.GtPath;
            _tbLocale.Text = Settings.Default.Locale;
            _tbRepoPath.Text = Settings.Default.RepoPath;
            UpdateRepoValidation();
        }

        private void FormSettings_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                Close();
        }

        private void _tbPathClick(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Settings.Default.DbcPath = folderBrowserDialog1.SelectedPath;
                Settings.Default.Save();
            }
        }

        private void _bBrowseRepo_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.SelectedPath = _tbRepoPath.Text;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                _tbRepoPath.Text = folderBrowserDialog1.SelectedPath;
                UpdateRepoValidation();
            }
        }

        private void _tbRepoPath_TextChanged(object sender, EventArgs e)
        {
            UpdateRepoValidation();
        }

        private void UpdateRepoValidation()
        {
            var path = _tbRepoPath.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                _lblRepoValidation.Text = "";
                return;
            }

            var sqlDir    = Path.Combine(path, "sql", "updates", "world", "WorldsoulPvP");
            var scriptDir = Path.Combine(path, "src", "server", "scripts", "WorldsoulPvP");

            if (Directory.Exists(sqlDir) && Directory.Exists(scriptDir))
            {
                _lblRepoValidation.Text      = "\u2713 Valid repo path";
                _lblRepoValidation.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                _lblRepoValidation.Text      = "\u2717 Missing required subdirectories";
                _lblRepoValidation.ForeColor = System.Drawing.Color.Red;
            }
        }
    }
}
