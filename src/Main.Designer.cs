namespace SubScreen
{
    partial class Main
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
            this.SuspendLayout();
            // 
            // Main
            // 
            // 无边框全屏：右上角不会出现最大化/还原按钮。
            // 窗口位置由代码按目标屏幕设定（见 Main.ApplyScreenBounds）。
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(1920, 1080);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Main";
            // 不在任务栏显示，入口放在托盘图标上（见 Main.SetupTray）。
            // 注意副作用：设成 false 之后 WinForms 会把窗口挂到一个隐藏的 owner 上，
            // Process.MainWindowHandle 会恒为 0 —— 所以 Program.TryActivateExisting
            // 找不回已有窗口，重复启动时不会把旧窗口叫到前面（托盘菜单仍可正常收回）。
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "SubScreen";
            this.ResumeLayout(false);
        }

        #endregion
    }
}
