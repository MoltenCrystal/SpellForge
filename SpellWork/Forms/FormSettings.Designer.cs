namespace SpellWork.Forms
{
    partial class FormSettings
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormSettings));
            this._gbDbSetting = new System.Windows.Forms.GroupBox();
            this._tbBase = new System.Windows.Forms.TextBox();
            this._lDbName = new System.Windows.Forms.Label();
            this._tbPass = new System.Windows.Forms.TextBox();
            this._lPassword = new System.Windows.Forms.Label();
            this._tbUser = new System.Windows.Forms.TextBox();
            this._lUser = new System.Windows.Forms.Label();
            this._tbPort = new System.Windows.Forms.TextBox();
            this._lPort = new System.Windows.Forms.Label();
            this._tbHost = new System.Windows.Forms.TextBox();
            this._lHost = new System.Windows.Forms.Label();
            this._cbUseDB = new System.Windows.Forms.CheckBox();
            this._bTestConnect = new System.Windows.Forms.Button();
            this._tbPath = new System.Windows.Forms.TextBox();
            this._lDb2Path = new System.Windows.Forms.Label();
            this._bSaveSettings = new System.Windows.Forms.Button();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this._lGtPath = new System.Windows.Forms.Label();
            this._tbGtPath = new System.Windows.Forms.TextBox();
            this._lLocale = new System.Windows.Forms.Label();
            this._tbLocale = new System.Windows.Forms.TextBox();
            this._cbReadOnlyDB = new System.Windows.Forms.CheckBox();
            this._gbDB2Settings = new System.Windows.Forms.GroupBox();
            this._gbDbSetting.SuspendLayout();
            this._gbDB2Settings.SuspendLayout();
            this.SuspendLayout();
            // 
            // _gbDbSetting
            // 
            this._gbDbSetting.Controls.Add(this._tbBase);
            this._gbDbSetting.Controls.Add(this._lDbName);
            this._gbDbSetting.Controls.Add(this._tbPass);
            this._gbDbSetting.Controls.Add(this._lPassword);
            this._gbDbSetting.Controls.Add(this._tbUser);
            this._gbDbSetting.Controls.Add(this._lUser);
            this._gbDbSetting.Controls.Add(this._tbPort);
            this._gbDbSetting.Controls.Add(this._lPort);
            this._gbDbSetting.Controls.Add(this._tbHost);
            this._gbDbSetting.Controls.Add(this._lHost);
            this._gbDbSetting.Location = new System.Drawing.Point(12, 12);
            this._gbDbSetting.Name = "_gbDbSetting";
            this._gbDbSetting.Size = new System.Drawing.Size(263, 149);
            this._gbDbSetting.TabIndex = 0;
            this._gbDbSetting.TabStop = false;
            this._gbDbSetting.Text = "Database Connection";
            // 
            // _tbBase
            // 
            this._tbBase.Location = new System.Drawing.Point(65, 122);
            this._tbBase.Name = "_tbBase";
            this._tbBase.Size = new System.Drawing.Size(192, 20);
            this._tbBase.TabIndex = 4;
            // 
            // _lDbName
            // 
            this._lDbName.AutoSize = true;
            this._lDbName.Location = new System.Drawing.Point(6, 125);
            this._lDbName.Name = "_lDbName";
            this._lDbName.Size = new System.Drawing.Size(53, 13);
            this._lDbName.TabIndex = 0;
            this._lDbName.Text = "Database";
            // 
            // _tbPass
            // 
            this._tbPass.Location = new System.Drawing.Point(65, 96);
            this._tbPass.Name = "_tbPass";
            this._tbPass.Size = new System.Drawing.Size(192, 20);
            this._tbPass.TabIndex = 3;
            this._tbPass.UseSystemPasswordChar = true;
            // 
            // _lPassword
            // 
            this._lPassword.AutoSize = true;
            this._lPassword.Location = new System.Drawing.Point(6, 99);
            this._lPassword.Name = "_lPassword";
            this._lPassword.Size = new System.Drawing.Size(30, 13);
            this._lPassword.TabIndex = 0;
            this._lPassword.Text = "Pass";
            // 
            // _tbUser
            // 
            this._tbUser.Location = new System.Drawing.Point(65, 70);
            this._tbUser.Name = "_tbUser";
            this._tbUser.Size = new System.Drawing.Size(192, 20);
            this._tbUser.TabIndex = 2;
            // 
            // _lUser
            // 
            this._lUser.AutoSize = true;
            this._lUser.Location = new System.Drawing.Point(6, 73);
            this._lUser.Name = "_lUser";
            this._lUser.Size = new System.Drawing.Size(29, 13);
            this._lUser.TabIndex = 0;
            this._lUser.Text = "User";
            // 
            // _tbPort
            // 
            this._tbPort.Location = new System.Drawing.Point(65, 44);
            this._tbPort.Name = "_tbPort";
            this._tbPort.Size = new System.Drawing.Size(192, 20);
            this._tbPort.TabIndex = 1;
            // 
            // _lPort
            // 
            this._lPort.AutoSize = true;
            this._lPort.Location = new System.Drawing.Point(6, 47);
            this._lPort.Name = "_lPort";
            this._lPort.Size = new System.Drawing.Size(52, 13);
            this._lPort.TabIndex = 0;
            this._lPort.Text = "Port/Pipe";
            // 
            // _tbHost
            // 
            this._tbHost.Location = new System.Drawing.Point(65, 18);
            this._tbHost.Name = "_tbHost";
            this._tbHost.Size = new System.Drawing.Size(192, 20);
            this._tbHost.TabIndex = 0;
            // 
            // _lHost
            // 
            this._lHost.AutoSize = true;
            this._lHost.Location = new System.Drawing.Point(6, 21);
            this._lHost.Name = "_lHost";
            this._lHost.Size = new System.Drawing.Size(29, 13);
            this._lHost.TabIndex = 0;
            this._lHost.Text = "Host";
            // 
            // _cbUseDB
            // 
            this._cbUseDB.AutoSize = true;
            this._cbUseDB.Location = new System.Drawing.Point(11, 171);
            this._cbUseDB.Name = "_cbUseDB";
            this._cbUseDB.Size = new System.Drawing.Size(63, 17);
            this._cbUseDB.TabIndex = 5;
            this._cbUseDB.Text = "Use DB";
            this._cbUseDB.UseVisualStyleBackColor = true;
            this._cbUseDB.CheckedChanged += new System.EventHandler(this.CbUseDbConnectCheckedChanged);
            // 
            // _bTestConnect
            // 
            this._bTestConnect.Location = new System.Drawing.Point(180, 167);
            this._bTestConnect.Name = "_bTestConnect";
            this._bTestConnect.Size = new System.Drawing.Size(95, 23);
            this._bTestConnect.TabIndex = 6;
            this._bTestConnect.Text = "Test connect";
            this._bTestConnect.UseVisualStyleBackColor = true;
            this._bTestConnect.Click += new System.EventHandler(this.BSaveSettingsClick);
            // 
            // _tbPath
            // 
            this._tbPath.Location = new System.Drawing.Point(65, 19);
            this._tbPath.Name = "_tbPath";
            this._tbPath.Size = new System.Drawing.Size(193, 20);
            this._tbPath.TabIndex = 6;
            this._tbPath.Click += new System.EventHandler(this._tbPathClick);
            // 
            // _lDb2Path
            // 
            this._lDb2Path.AutoSize = true;
            this._lDb2Path.Location = new System.Drawing.Point(6, 22);
            this._lDb2Path.Name = "_lDb2Path";
            this._lDb2Path.Size = new System.Drawing.Size(53, 13);
            this._lDb2Path.TabIndex = 5;
            this._lDb2Path.Text = "DB2 Path";
            // 
            // _bSaveSettings
            // 
            this._bSaveSettings.Location = new System.Drawing.Point(180, 297);
            this._bSaveSettings.Name = "_bSaveSettings";
            this._bSaveSettings.Size = new System.Drawing.Size(95, 23);
            this._bSaveSettings.TabIndex = 7;
            this._bSaveSettings.Text = "Save";
            this._bSaveSettings.UseVisualStyleBackColor = true;
            this._bSaveSettings.Click += new System.EventHandler(this.BSaveSettingsClick);
            // 
            // folderBrowserDialog1
            // 
            this.folderBrowserDialog1.RootFolder = System.Environment.SpecialFolder.DesktopDirectory;
            this.folderBrowserDialog1.SelectedPath = ".";
            this.folderBrowserDialog1.ShowNewFolderButton = false;
            // 
            // _lGtPath
            // 
            this._lGtPath.AutoSize = true;
            this._lGtPath.Location = new System.Drawing.Point(6, 48);
            this._lGtPath.Name = "_lGtPath";
            this._lGtPath.Size = new System.Drawing.Size(47, 13);
            this._lGtPath.TabIndex = 8;
            this._lGtPath.Text = "GT Path";
            // 
            // _tbGtPath
            // 
            this._tbGtPath.Location = new System.Drawing.Point(65, 45);
            this._tbGtPath.Name = "_tbGtPath";
            this._tbGtPath.Size = new System.Drawing.Size(193, 20);
            this._tbGtPath.TabIndex = 9;
            // 
            // _lLocale
            // 
            this._lLocale.AutoSize = true;
            this._lLocale.Location = new System.Drawing.Point(6, 74);
            this._lLocale.Name = "_lLocale";
            this._lLocale.Size = new System.Drawing.Size(39, 13);
            this._lLocale.TabIndex = 10;
            this._lLocale.Text = "Locale";
            // 
            // _tbLocale
            // 
            this._tbLocale.Location = new System.Drawing.Point(65, 71);
            this._tbLocale.Name = "_tbLocale";
            this._tbLocale.Size = new System.Drawing.Size(193, 20);
            this._tbLocale.TabIndex = 11;
            // 
            // _cbReadOnlyDB
            // 
            this._cbReadOnlyDB.AutoSize = true;
            this._cbReadOnlyDB.Location = new System.Drawing.Point(80, 171);
            this._cbReadOnlyDB.Name = "_cbReadOnlyDB";
            this._cbReadOnlyDB.Size = new System.Drawing.Size(94, 17);
            this._cbReadOnlyDB.TabIndex = 12;
            this._cbReadOnlyDB.Text = "DB Read Only";
            this._cbReadOnlyDB.UseVisualStyleBackColor = true;
            // 
            // _gbDB2Settings
            // 
            this._gbDB2Settings.Controls.Add(this._lDb2Path);
            this._gbDB2Settings.Controls.Add(this._tbPath);
            this._gbDB2Settings.Controls.Add(this._tbLocale);
            this._gbDB2Settings.Controls.Add(this._lGtPath);
            this._gbDB2Settings.Controls.Add(this._lLocale);
            this._gbDB2Settings.Controls.Add(this._tbGtPath);
            this._gbDB2Settings.Location = new System.Drawing.Point(12, 194);
            this._gbDB2Settings.Name = "_gbDB2Settings";
            this._gbDB2Settings.Size = new System.Drawing.Size(263, 97);
            this._gbDB2Settings.TabIndex = 13;
            this._gbDB2Settings.TabStop = false;
            this._gbDB2Settings.Text = "DB2";
            // 
            // FormSettings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(287, 332);
            this.Controls.Add(this._gbDB2Settings);
            this.Controls.Add(this._cbReadOnlyDB);
            this.Controls.Add(this._bSaveSettings);
            this.Controls.Add(this._bTestConnect);
            this.Controls.Add(this._cbUseDB);
            this.Controls.Add(this._gbDbSetting);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(303, 371);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(303, 371);
            this.Name = "FormSettings";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "SpellWork Settings";
            this.Load += new System.EventHandler(this.SettingsFormLoad);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FormSettings_KeyDown);
            this._gbDbSetting.ResumeLayout(false);
            this._gbDbSetting.PerformLayout();
            this._gbDB2Settings.ResumeLayout(false);
            this._gbDB2Settings.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox _gbDbSetting;
        private System.Windows.Forms.Label _lHost;
        private System.Windows.Forms.TextBox _tbBase;
        private System.Windows.Forms.Label _lDbName;
        private System.Windows.Forms.TextBox _tbPass;
        private System.Windows.Forms.Label _lPassword;
        private System.Windows.Forms.TextBox _tbUser;
        private System.Windows.Forms.Label _lUser;
        private System.Windows.Forms.TextBox _tbPort;
        private System.Windows.Forms.Label _lPort;
        private System.Windows.Forms.TextBox _tbHost;
        private System.Windows.Forms.CheckBox _cbUseDB;
        private System.Windows.Forms.Button _bTestConnect;
        private System.Windows.Forms.Button _bSaveSettings;
        private System.Windows.Forms.TextBox _tbPath;
        private System.Windows.Forms.Label _lDb2Path;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.Label _lGtPath;
        private System.Windows.Forms.TextBox _tbGtPath;
        private System.Windows.Forms.Label _lLocale;
        private System.Windows.Forms.TextBox _tbLocale;
        private System.Windows.Forms.CheckBox _cbReadOnlyDB;
        private System.Windows.Forms.GroupBox _gbDB2Settings;
    }
}